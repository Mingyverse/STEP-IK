using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using StepIK.Sandbox;

namespace StepIK.Editor
{
    /// <summary>Run explicitly from the Unity menu; never edits a user's existing scene in place.</summary>
    public static class StepIKSandboxBuilder
    {
        private static bool waitingForFonts;
        private static string folder;
        private static TMP_FontAsset font;
        private static Sprite roundedSurface;
        private static Material groundMaterial, meadowMaterial, rockMaterial, accentMaterial, quarryMaterial, badlandsMaterial;
        private static readonly Color PracticeSand = new Color(0.54f, 0.46f, 0.34f);
        private const string CityPeople = "Assets/DenysAlmaral/CityPeople/";

        [MenuItem("Tools/STEP-IK/Build Comparison Sandbox")]
        public static void Build() => Build(false);

        [MenuItem("Tools/STEP-IK/Upgrade Open Studio Controls")]
        public static void UpgradeOpenStudioControls()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode to upgrade the controls.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var manager = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<StepIKSandbox>(true)).Single();
            font = manager.metricsLabel.font;
            roundedSurface = manager.matrixButton.GetComponent<UnityEngine.UI.Image>().sprite;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade STEP-IK Controls");
            Undo.RecordObject(manager, "Assign STEP-IK controls");
            if (!manager.speedSlider)
            {
                var old = manager.speedChoice;
                if (!old) throw new InvalidOperationException("The Studio speed dropdown is missing.");
                Transform parent = old.transform.parent;
                int sibling = old.transform.GetSiblingIndex();
                BuildSpeedSlider(parent, manager);
                var newRoot = manager.speedSlider.transform.parent.gameObject;
                newRoot.transform.SetSiblingIndex(sibling);
                Undo.RegisterCreatedObjectUndo(newRoot, "Add walk speed slider");
                Undo.DestroyObjectImmediate(old.gameObject);
                manager.speedChoice = null;
            }
            if (!manager.sprintButton)
            {
                var old = manager.manualButton.transform;
                int sibling = old.GetSiblingIndex();
                var row = ActionRow(old.parent, "Manual Actions", 44f);
                row.SetSiblingIndex(sibling);
                Undo.RegisterCreatedObjectUndo(row.gameObject, "Add manual actions");
                Undo.SetTransformParent(old, row, "Group manual actions");
                manager.sprintButton = Button(row, "Sprint", "Sprint · Shift", out manager.sprintLabel, 44f);
            }
            var canvas = manager.metricsLabel.canvas;
            var title = canvas.transform.Find("Header/Title").GetComponent<TMP_Text>();
            Undo.RecordObject(title, "Rename STEP-IK title");
            title.text = "STEP-IK    /    LOCOMOTION LAB";
            Undo.RecordObject(manager.gameObject, "Rename STEP-IK manager");
            manager.gameObject.name = "STEP-IK Comparison Sandbox";
            foreach (var actor in manager.cast)
            {
                var animator = actor.animator;
                var controller = animator.runtimeAnimatorController as AnimatorController;
                if (!controller) throw new InvalidOperationException("The Studio character needs an AnimatorController.");
                var stateMachine = controller.layers[0].stateMachine;
                var state = stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Run");
                if (!state) state = stateMachine.AddState("Run");
                var run = LoadClip(actor.name.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "locom_f_running_20f" : "locom_m_running_20f");
                state.motion = run;
                var profile = actor.GetComponent<CrowdProfile>();
                Undo.RecordObject(profile, "Add sprint contact curves");
                profile.runSeconds = run.length;
                bool wasActive = actor.gameObject.activeSelf;
                try
                {
                    actor.gameObject.SetActive(true);
                    SampleStance(profile, animator, run, actor, true);
                }
                finally { actor.gameObject.SetActive(wasActive); }
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(controller);
            }
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Canvas.ForceUpdateCanvases();
        }

        public static string Build(bool preserveOpenScenes)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorUtility.DisplayDialog("STEP-IK", "Exit Play mode before building the sandbox.", "OK"); return null; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { Debug.LogWarning("Wait for Unity to finish compiling/importing, then run Build Comparison Sandbox again."); return null; }
            font = TMP_Settings.defaultFontAsset;
            if (!font)
            {
                string guid = AssetDatabase.FindAssets("LiberationSans SDF t:TMP_FontAsset").FirstOrDefault();
                if (guid != null) font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (!font)
            {
                if (waitingForFonts) return null;
                waitingForFonts = true;
                AssetDatabase.importPackageCompleted += OnResourcesImported;
                AssetDatabase.importPackageFailed += OnResourcesFailed;
                try { TMP_PackageResourceImporter.ImportResources(true, false, false); }
                catch (Exception error) { OnResourcesFailed("TMP Essential Resources", error.Message); return null; }
                Debug.Log("Importing TMP font essentials. Sandbox construction will resume after import.");
                return null;
            }
            if (!preserveOpenScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(CityPeople + "Prefabs/downtown/casual_Male_K.prefab"))
                throw new InvalidOperationException("Import City People FREE Samples from Package Manager, then build the sandbox.");
            // Every invocation gets a new folder; existing generated scenes/assets are not overwritten.
            if (!AssetDatabase.IsValidFolder("Assets/StepIKGenerated")) AssetDatabase.CreateFolder("Assets", "StepIKGenerated");
            string leaf = "Sandbox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            AssetDatabase.CreateFolder("Assets/StepIKGenerated", leaf);
            folder = "Assets/StepIKGenerated/" + leaf;
            roundedSurface = CreateRoundedSurface();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, preserveOpenScenes ? NewSceneMode.Additive : NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            groundMaterial = Material("Benchmark floor - warm sand", PracticeSand);
            meadowMaterial = Material("Meadow", new Color(0.26f, 0.42f, 0.18f));
            quarryMaterial = Material("Quarry gravel", new Color(0.39f, 0.45f, 0.47f));
            badlandsMaterial = Material("Badlands sandstone", new Color(0.67f, 0.34f, 0.21f));
            rockMaterial = Material("Stone", new Color(0.29f, 0.31f, 0.25f));
            accentMaterial = Material("Markers - amber", new Color(1f, 0.66f, 0.18f));

            var manager = new GameObject("STEP-IK Comparison Sandbox").AddComponent<StepIKSandbox>();
            manager.generatedAssetFolder = folder;
            manager.terrainRoots = BuildTerrains();
            manager.useRoughWorlds = true;
            string[] paths = { "downtown/casual_Male_K", "downtown/casual_Female_K", "city/casual_Male_G",
                "city/casual_Female_G", "professions/Doctor_Male_B", "little_kids/little_boy_B" };
            manager.cast = paths.Select(path => BuildCharacter(path)).ToArray();
            manager.template = manager.cast[0];
            manager.authoredCharacter = true;
            foreach (var actor in manager.cast) actor.gameObject.SetActive(false);
            manager.viewCamera = BuildCamera();
            BuildLighting();
            BuildUI(manager);
            for (int i = 0; i < manager.terrainRoots.Length; i++) manager.terrainRoots[i].SetActive(i == 0);
            AssetDatabase.SaveAssets();
            string scenePath = folder + "/STEP-IK Comparison.unity";
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Could not save the sandbox scene.");
            Selection.activeGameObject = manager.gameObject;
            Validate(manager);
            Debug.Log("STEP-IK sandbox created at " + scenePath + " with City People animations. Press Play.");
            return scenePath;
        }

        private static void OnResourcesImported(string package)
        {
            if (!package.Contains("TMP Essential")) return;
            waitingForFonts = false;
            AssetDatabase.importPackageCompleted -= OnResourcesImported;
            AssetDatabase.importPackageFailed -= OnResourcesFailed;
            EditorApplication.delayCall += Build;
        }
        private static void OnResourcesFailed(string package, string error)
        {
            waitingForFonts = false;
            AssetDatabase.importPackageCompleted -= OnResourcesImported;
            AssetDatabase.importPackageFailed -= OnResourcesFailed;
            Debug.LogError("Font resource import failed: " + error + ". Retry the sandbox menu after resolving it.");
        }

        private static Material Material(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("No supported Lit shader available.");
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
            return material;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer = collision ? 0 : 2; // Ground = Default; visuals = Ignore Raycast.
            return go;
        }

        private static GameObject[] BuildTerrains()
        {
            var result = new GameObject[StepIKSandbox.Terrains.Length];
            var defaultFloor = BuildPracticeFloorMaterial();
            var frost = BuildLandscapeMaterial(7, "Frost Basin");
            var mosswood = BuildLandscapeMaterial(8, "Mosswood");
            var surfaceMaterials = new[]
            {
                defaultFloor, groundMaterial, groundMaterial, groundMaterial, meadowMaterial,
                quarryMaterial, badlandsMaterial, frost, mosswood
            };
            for (int type = 0; type < result.Length; type++)
            {
                var root = new GameObject(StepIKSandbox.Terrains[type]); result[type] = root;
                if (type == 0) Box("Empty world floor", root.transform, new Vector3(0f, -0.15f, 12f), new Vector3(60f, 0.3f, 48f), defaultFloor, true);
                else if (type == 2)
                {
                    // Half-metre boxes keep the vertical stair risers exact, rather than interpolating a ramp.
                    for (int step = -2; step < 42; step++)
                    {
                        float z = step * 0.5f + 0.25f;
                        float top = StepIKSandbox.SurfaceHeight(type, 0f, z);
                        Box("Tread " + step, root.transform, new Vector3(0f, (top - 0.3f) * 0.5f, z),
                            new Vector3(60f, top + 0.3f, 0.5f), groundMaterial, true);
                    }
                }
                else
                {
                    bool openWorld = type >= 4;
                    float xStep = type >= 3 ? SandboxTerrain.GridX : 0.5f;
                    int nx = Mathf.RoundToInt(60f / xStep), nz = openWorld ? 192 : 220;
                    var vertices = new Vector3[(nx + 1) * (nz + 1)];
                    var uv = new Vector2[vertices.Length];
                    var triangles = new int[nx * nz * 6]; int k = 0;
                    for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
                    {
                        float px = -30f + x * xStep;
                        float pz = openWorld ? -12f + z * 0.25f : -1f + z * 0.1f;
                        int v = z * (nx + 1) + x;
                        vertices[v] = new Vector3(px, StepIKSandbox.SurfaceHeight(type, px, pz), pz);
                        uv[v] = type >= 7
                            ? new Vector2((px + 30f) / 60f, (pz + 12f) / 48f)
                            : new Vector2(px, pz);
                        if (x == nx || z == nz) continue;
                        triangles[k++] = v; triangles[k++] = v + nx + 1; triangles[k++] = v + 1;
                        triangles[k++] = v + 1; triangles[k++] = v + nx + 1; triangles[k++] = v + nx + 2;
                    }
                    var mesh = new Mesh { name = StepIKSandbox.Terrains[type] + " mesh", vertices = vertices, uv = uv, triangles = triangles };
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, folder + "/Terrain" + type + ".asset");
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    root.AddComponent<MeshRenderer>().sharedMaterial = surfaceMaterials[type];
                    root.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
                if (type > 0 && type < 4)
                {
                    Box("South approach", root.transform, new Vector3(0f, -0.15f, -6.5f), new Vector3(60f, 0.3f, 11f), groundMaterial, true);
                    Box("North approach", root.transform, new Vector3(0f, -0.15f, 28.5f), new Vector3(60f, 0.3f, 15f), groundMaterial, true);
                }
                if (type == 4) DecorateMeadow(root.transform);
                if (type == 5 || type == 6) DecorateRidge(root.transform, type, type == 5 ? rockMaterial : badlandsMaterial);
                if (type == 7) DecorateFrostBasin(root.transform);
                if (type == 8) DecorateMosswood(root.transform);
                for (int z = 0; type > 0 && type < 4 && z <= 18; z += 2)
                {
                    float y = StepIKSandbox.SurfaceHeight(type, 0f, z) + 0.012f;
                    Box("Distance marker " + z, root.transform, new Vector3(-3.9f, y, z), new Vector3(0.14f, 0.02f, 0.07f), accentMaterial, false);
                    Box("Distance marker " + z + " right", root.transform, new Vector3(3.9f, y, z), new Vector3(0.14f, 0.02f, 0.07f), accentMaterial, false);
                }
            }
            return result;
        }

        private static Material BuildPracticeFloorMaterial()
        {
            var material = Material("Practice floor - warm sand", PracticeSand);
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                byte level = (byte)(x == 0 || y == 0 ? 207 : 255);
                pixels[y * size + x] = new Color32(level, level, level, 255);
            }
            SetSurfaceTexture(material, "Practice floor grid", size, pixels, TextureWrapMode.Repeat,
                new Vector2(30f, 24f));
            return material;
        }

        private static Material BuildLandscapeMaterial(int type, string name)
        {
            var material = Material(name, Color.white);
            const int size = 192;
            var pixels = new Color32[size * size];
            for (int zi = 0; zi < size; zi++)
            for (int xi = 0; xi < size; xi++)
            {
                float x = Mathf.Lerp(-30f, 30f, xi / (float)(size - 1));
                float z = Mathf.Lerp(-12f, 36f, zi / (float)(size - 1));
                float fine = Mathf.PerlinNoise(x * 1.35f + 67f, z * 1.1f + 21f);
                Color tint;
                if (type == 7)
                {
                    float distance = Mathf.Abs(x - SandboxTerrain.FrostChannelX(z));
                    float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 2.05f, distance));
                    var ice = Color.Lerp(new Color(0.20f, 0.48f, 0.64f), new Color(0.46f, 0.72f, 0.81f), fine);
                    tint = Color.Lerp(ice, new Color(0.76f, 0.85f, 0.90f), snow);
                }
                else
                {
                    float distance = Mathf.Abs(x - SandboxTerrain.MosswoodTrailX(z));
                    float moss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 2.3f, distance + fine * 0.55f));
                    var soil = Color.Lerp(new Color(0.33f, 0.26f, 0.17f), new Color(0.53f, 0.43f, 0.29f), fine);
                    var canopy = Color.Lerp(new Color(0.16f, 0.27f, 0.15f), new Color(0.43f, 0.51f, 0.25f), fine);
                    tint = Color.Lerp(soil, canopy, moss);
                }
                pixels[zi * size + xi] = tint;
            }
            SetSurfaceTexture(material, name + " ground tint", size, pixels, TextureWrapMode.Clamp, Vector2.one);
            return material;
        }

        private static void SetSurfaceTexture(Material material, string name, int size, Color32[] pixels,
            TextureWrapMode wrap, Vector2 scale)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            AssetDatabase.CreateAsset(texture, folder + "/" + name + ".asset");
            string key = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            material.SetTexture(key, texture);
            material.SetTextureScale(key, scale);
            EditorUtility.SetDirty(material);
        }

        private static void DecorateFrostBasin(Transform root)
        {
            var crystal = Material("Basin ice", new Color(0.42f, 0.71f, 0.82f));
            var snow = Material("Basin snow", new Color(0.82f, 0.89f, 0.92f));
            var random = new System.Random(877);
            for (int i = 0; i < 37; i++)
            {
                float x = i < 22 ? (i % 2 == 0 ? -1f : 1f) * (32f + i % 3) : -36f + (i - 22) * 5f;
                float z = i < 22 ? -10f + i * 2.4f : 39f + (float)random.NextDouble() * 7f;
                float height = 2.1f + (float)random.NextDouble() * 6.2f;
                var ice = Box("Tilted ice shelf", root, new Vector3(x, height * 0.29f, z),
                    new Vector3(2.6f, height, 3.4f), i % 3 == 0 ? snow : crystal, false);
                ice.transform.localRotation = Quaternion.Euler(7f + i % 6 * 3f, i * 47f, (i % 5 - 2f) * 8f);
            }
        }

        private static void DecorateMosswood(Transform root)
        {
            var bark = Material("Mosswood bark", new Color(0.28f, 0.19f, 0.12f));
            var pine = Material("Mosswood pine", new Color(0.13f, 0.30f, 0.18f));
            var cedar = Material("Mosswood cedar", new Color(0.28f, 0.40f, 0.20f));
            var crown = BuildConeMesh();
            var random = new System.Random(1507);
            for (int i = 0; i < 56; i++)
            {
                float x = i < 36 ? (i % 2 == 0 ? -1f : 1f) * (30f + i % 4) : -38f + (i - 36) * 4f;
                float z = i < 36 ? -11f + i * 1.48f : 37f + (float)random.NextDouble() * 13f;
                float height = 3.4f + (float)random.NextDouble() * 4.2f;
                float baseY = SandboxTerrain.HeightAt(8, x, z) - 0.22f;
                Box("Forest trunk", root, new Vector3(x, baseY + 0.40f * height, z),
                    new Vector3(0.18f * height, 0.8f * height, 0.18f * height), bark, false);
                for (int tier = 0; tier < 2; tier++)
                {
                    var canopy = new GameObject("Conifer crown");
                    canopy.layer = 2;
                    canopy.transform.SetParent(root, false);
                    canopy.transform.localPosition = new Vector3(x, baseY + height * (0.33f + tier * 0.23f), z);
                    canopy.transform.localScale = new Vector3(height * (0.53f - tier * 0.14f), height * 0.53f,
                        height * (0.53f - tier * 0.14f));
                    canopy.transform.localRotation = Quaternion.Euler(0f, i * 49f + 15f * tier, 0f);
                    canopy.AddComponent<MeshFilter>().sharedMesh = crown;
                    canopy.AddComponent<MeshRenderer>().sharedMaterial = (i + tier) % 3 == 0 ? cedar : pine;
                }
            }
        }

        private static Mesh BuildConeMesh()
        {
            const int sides = 7;
            var vertices = new Vector3[sides * 3];
            var triangles = new int[vertices.Length];
            for (int side = 0; side < sides; side++)
            {
                float a = 2f * Mathf.PI * side / sides;
                float b = 2f * Mathf.PI * (side + 1) / sides;
                int index = side * 3;
                vertices[index] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
                vertices[index + 1] = Vector3.up;
                vertices[index + 2] = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 0.5f;
                triangles[index] = index;
                triangles[index + 1] = index + 1;
                triangles[index + 2] = index + 2;
            }
            var mesh = new Mesh { name = "Faceted conifer crown", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, folder + "/Faceted conifer crown.asset");
            return mesh;
        }

        private static void DecorateRidge(Transform root, int type, Material material)
        {
            var random = new System.Random(240 + type);
            for (int i = 0; i < 32; i++)
            {
                float x = -39f + i * 2.5f;
                float z = 40f + (float)random.NextDouble() * 12f;
                float height = 1.8f + (float)random.NextDouble() * (type == 5 ? 5f : 9f);
                var ridge = Box(type == 5 ? "Quarry ridge" : "Sandstone mesa", root,
                    new Vector3(x, 0.15f * height, z), new Vector3(5f, height, 10f), material, false);
                ridge.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 40f - 20f, 0f);
            }
        }

        private static void DecorateMeadow(Transform root)
        {
            var wood = Material("Bark", new Color(0.28f, 0.18f, 0.10f));
            var leaves = Material("Pine", new Color(0.10f, 0.29f, 0.18f));
            var random = new System.Random(741);
            for (int i = 0; i < 38; i++)
            {
                float x = i < 26 ? (i % 2 == 0 ? -1f : 1f) * (30f + i % 3) : (float)random.NextDouble() * 60f - 30f;
                float z = i < 26 ? (float)random.NextDouble() * 63f - 14f : 36f + i % 5;
                float h = 1.8f + (float)random.NextDouble() * 1.8f;
                float y = SandboxTerrain.HeightAt(4, x, z) - 0.2f;
                Box("Pine trunk", root, new Vector3(x, y + h * 0.48f, z), new Vector3(0.28f, h, 0.28f), wood, false);
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Pine canopy";
                crown.transform.SetParent(root, false);
                crown.transform.localPosition = new Vector3(x, y + h, z);
                crown.transform.localScale = new Vector3(h * 0.65f, h, h * 0.65f);
                crown.GetComponent<Renderer>().sharedMaterial = leaves;
                UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());
            }
            for (int i = 0; i < 12; i++)
            {
                float x = -36f + i * 6.5f;
                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Distant ridge";
                hill.transform.SetParent(root, false);
                hill.transform.localPosition = new Vector3(x, -1.6f, 43f + i % 3 * 4f);
                hill.transform.localScale = new Vector3(16f, 9f + i % 4 * 2f, 20f);
                hill.GetComponent<Renderer>().sharedMaterial = rockMaterial;
                UnityEngine.Object.DestroyImmediate(hill.GetComponent<Collider>());
            }
        }

        private static StepIKController BuildCharacter(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CityPeople + "Prefabs/" + path + ".prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = prefab.name.Replace('_', ' ');
            var animator = root.GetComponent<Animator>();
            if (!animator || !animator.isHuman) throw new InvalidOperationException("City People character requires a Humanoid avatar at its root.");
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            PrepareCharacterMaterials(root);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;

            bool female = path.Contains("Female");
            var walk = LoadClip(female ? "locom_f_basicWalk_30f" : "locom_m_basicWalk_30f");
            var idle = LoadClip(female ? "idle_f_1_150f" : "idle_m_1_200f");
            var run = LoadClip(female ? "locom_f_running_20f" : "locom_m_running_20f");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/" + prefab.name + ".controller");
            var layers = controller.layers;
            var stateMachine = layers[0].stateMachine;
            var state = stateMachine.AddState("Walk");
            state.motion = walk;
            stateMachine.defaultState = state;
            stateMachine.AddState("Idle").motion = idle;
            stateMachine.AddState("Run").motion = run;
            layers[0].iKPass = true;
            animator.runtimeAnimatorController = controller;
            controller.layers = layers;
            var ik = root.AddComponent<StepIKController>();
            ik.animator = animator;
            ik.pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
            ConfigureSole(ik.leftLeg, animator, true);
            ConfigureSole(ik.rightLeg, animator, false);
            ik.settings.terrainMask = 1;
            ik.settings.useHeightBasedSwingFade = false;
            ik.settings.maxFootCorrection = 0.6f;
            ik.testMode = TestMode.FKOnly;
            var profile = root.AddComponent<CrowdProfile>();
            profile.walkSeconds = walk.length;
            profile.idleSeconds = idle.length;
            profile.runSeconds = run.length;
            SampleStance(profile, animator, walk, ik);
            SampleStance(profile, animator, run, ik, true);
            return ik;
        }

        private static void PrepareCharacterMaterials(GameObject root)
        {
            var converted = new Dictionary<Material, Material>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    var source = materials[index];
                    if (!source || source.shader.name.StartsWith("Universal Render Pipeline/")) continue;
                    if (!converted.TryGetValue(source, out var material))
                    {
                        material = new Material(shader) { name = source.name + " (Sandbox URP)" };
                        if (source.HasProperty("_MainTex")) material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                        else if (source.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                        if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.GetColor("_Color"));
                        else if (source.HasProperty("_BaseColor")) material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                        material.SetFloat("_Smoothness", 0.12f);
                        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(folder + "/Character Material.mat"));
                        converted.Add(source, material);
                    }
                    materials[index] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static AnimationClip LoadClip(string name)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(CityPeople + "Animations/" + name + ".fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (!clip || !clip.humanMotion) throw new InvalidOperationException("Missing Humanoid animation: " + name);
            return clip;
        }

        private static void ConfigureSole(LegReferences leg, Animator animator, bool left)
        {
            leg.upperLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            leg.lowerLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            leg.foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            var toes = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            Vector3 ankle = leg.foot.position;
            Vector3 forward = animator.transform.forward;
            float length = toes ? Mathf.Abs(Vector3.Dot(toes.position - ankle, forward)) : 0.14f;
            leg.soleHeight = Mathf.Clamp(ankle.y - animator.transform.position.y, 0.025f, 0.18f);
            Vector3 sole = ankle - Vector3.up * leg.soleHeight;
            leg.heelOffset = leg.foot.InverseTransformPoint(sole - forward * length * 0.45f);
            leg.toeOffset = leg.foot.InverseTransformPoint(sole + forward * length * 1.22f);
            leg.hasCalibratedRotation = true;
            leg.restRotation = Quaternion.Inverse(animator.transform.rotation) * leg.foot.rotation;
        }

        private static void SampleStance(CrowdProfile profile, Animator animator, AnimationClip clip, StepIKController ik, bool run = false)
        {
            const int count = 60;
            var left = new float[count + 1];
            var right = new float[count + 1];
            var transforms = animator.GetComponentsInChildren<Transform>();
            var positions = transforms.Select(t => t.localPosition).ToArray();
            var rotations = transforms.Select(t => t.localRotation).ToArray();
            var graph = PlayableGraph.Create("Sample authored walk");
            try
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                graph.Play();
                for (int i = 0; i < count; i++)
                {
                    playable.SetTime(i * clip.length / count);
                    graph.Evaluate(0f);
                    left[i] = SoleHeight(ik.leftLeg, animator.transform);
                    right[i] = SoleHeight(ik.rightLeg, animator.transform);
                }
            }
            finally
            {
                graph.Destroy();
                for (int i = 0; i < transforms.Length; i++)
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
            }
            left[count] = left[0];
            right[count] = right[0];
            if (run)
            {
                profile.leftRunStance = StanceCurve(left);
                profile.rightRunStance = StanceCurve(right);
            }
            else
            {
                profile.leftStance = StanceCurve(left);
                profile.rightStance = StanceCurve(right);
            }
        }

        private static float SoleHeight(LegReferences leg, Transform root)
        {
            float heel = leg.foot.TransformPoint(leg.heelOffset).y;
            float toe = leg.foot.TransformPoint(leg.toeOffset).y;
            return Mathf.Min(heel, toe) - root.position.y;
        }

        private static AnimationCurve StanceCurve(float[] height)
        {
            float minimum = height.Min(), range = height.Max() - minimum;
            if (range < 0.015f) throw new InvalidOperationException("Walk sampling did not produce enough foot motion to derive stance.");
            var keys = height.Select((y, i) =>
            {
                float lift = Mathf.InverseLerp(0.16f, 0.42f, (y - minimum) / range);
                return new Keyframe(i / (float)(height.Length - 1), 1f - Mathf.SmoothStep(0f, 1f, lift));
            }).ToArray();
            return new AnimationCurve(keys);
        }

        private static Camera BuildCamera()
        {
            var go = new GameObject("Sandbox Camera"); go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.backgroundColor = new Color(0.055f, 0.08f, 0.11f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 48f; camera.nearClipPlane = 0.05f; camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(3.6f, 2.3f, -2.8f);
            camera.transform.LookAt(new Vector3(0f, 0.8f, 1f));
            return camera;
        }
        private static void BuildLighting()
        {
            var light = new GameObject("Key Light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.58f);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        private static void Fill(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding); rect.offsetMax = new Vector2(-padding, -padding);
        }
        private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent); rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = new Vector2(18f, 18f); rect.offsetMax = new Vector2(-18f, -18f);
            RoundedImage(rect, new Color(0.027f, 0.055f, 0.079f, 0.96f), false);
            return rect;
        }

        private static Sprite CreateRoundedSurface()
        {
            const int size = 48;
            const float radius = 14f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Rounded surface texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var center = new Vector2(Mathf.Clamp(point.x, radius, size - radius),
                    Mathf.Clamp(point.y, radius, size - radius));
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(radius + 0.5f - Vector2.Distance(point, center)));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            AssetDatabase.CreateAsset(texture, folder + "/Rounded UI Surface.asset");
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(15f, 15f, 15f, 15f));
            sprite.name = "Rounded surface";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            return sprite;
        }

        private static UnityEngine.UI.Image RoundedImage(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = roundedSurface;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }
        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color)
        {
            var rect = Rect(name, parent); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow; return text;
        }
        private static UnityEngine.UI.Button Button(Transform parent, string name, string caption, out TMP_Text label, float height = 72f)
        {
            var rect = Rect(name, parent);
            var image = RoundedImage(rect, new Color(0.14f, 0.24f, 0.30f), true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.78f, 1f, 0.95f);
            colors.pressedColor = new Color(0.60f, 0.82f, 0.76f);
            button.colors = colors;
            var layout = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); layout.preferredHeight = height;
            label = Text("Label", rect, caption, 18f, Color.white); Fill(label.rectTransform, 10f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return button;
        }

        private static void BuildUI(StepIKSandbox manager)
        {
            var canvas = new GameObject("Sandbox HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 1000f); scaler.matchWidthOrHeight = 0.5f;
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var header = Panel("Header", canvas.transform, new Vector2(0f, 0.89f), Vector2.one);
            var title = Text("Title", header, "STEP-IK    /    LOCOMOTION LAB", 29f, new Color(0.36f, 0.94f, 0.81f));
            Fill(title.rectTransform, 16f); title.alignment = TextAlignmentOptions.MidlineLeft;
            var left = Panel("Controls", canvas.transform, new Vector2(0f, 0.13f), new Vector2(0.25f, 0.89f));
            var layout = left.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12); layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandHeight = false;
            SectionLabel(left, "WALK SETUP");
            manager.methodChoice = Dropdown(left, "Method");
            manager.worldChoice = Dropdown(left, "World");
            manager.crowdInput = CrowdSizeField(left);
            BuildSpeedSlider(left, manager);
            manager.cameraChoice = Dropdown(left, "Camera");
            manager.castChoice = Dropdown(left, "Cast");
            var manualActions = ActionRow(left, "Manual Actions", 44f);
            manager.manualButton = Button(manualActions, "Manual Walk", "Manual walk", out manager.manualLabel, 44f);
            manager.sprintButton = Button(manualActions, "Sprint", "Sprint · Shift", out manager.sprintLabel, 44f);
            var previewActions = ActionRow(left, "Preview Actions", 42f);
            manager.playButton = Button(previewActions, "Pause", "Pause / play", out _, 42f);
            manager.resetButton = Button(previewActions, "Reset", "Reset / cancel", out _, 42f);

            SectionLabel(left, "RECORD & COMPARE");
            manager.benchmarkChoice = Dropdown(left, "Benchmark size");
            var recordActions = ActionRow(left, "Recording Actions", 44f);
            manager.runButton = Button(recordActions, "Run Selected", "This method", out _, 44f);
            manager.suiteButton = Button(recordActions, "Run Suite", $"All {StepIKSandbox.Methods.Length} here", out _, 44f);
            manager.matrixButton = Button(left, "Run Terrain Benchmark", "Benchmark all methods", out _, 48f);
            var benchmarkImage = manager.matrixButton.GetComponent<UnityEngine.UI.Image>();
            benchmarkImage.color = new Color(0.12f, 0.47f, 0.38f);
            manager.exportButton = Button(left, "Open Results", "Open results folder", out _, 42f);

            var right = Panel("Metrics", canvas.transform, new Vector2(0.75f, 0.29f), new Vector2(1f, 0.89f));
            manager.metricsLabel = Text("Live Metrics", right, "LIVE DIAGNOSTICS\n\nPress Play to begin.", 20f, Color.white);
            Fill(manager.metricsLabel.rectTransform, 20f);
            var notes = Panel("Measurement Notes", canvas.transform, new Vector2(0.75f, 0.13f), new Vector2(1f, 0.29f));
            manager.noteLabel = Text("Notes", notes, "Prototype only. No benchmark conclusions yet.", 16f, new Color(1f, 0.8f, 0.4f));
            Fill(manager.noteLabel.rectTransform, 12f);
            var footer = Panel("Status", canvas.transform, Vector2.zero, new Vector2(1f, 0.13f));
            manager.statusLabel = Text("Run Status", footer, "Press Play to start. Drag in the scene to orbit the camera.", 21f, Color.white);
            Fill(manager.statusLabel.rectTransform, 16f); manager.statusLabel.alignment = TextAlignmentOptions.MidlineLeft;

            var overlay = Rect("Completed Results", canvas.transform);
            Fill(overlay, 0f);
            var blocker = overlay.gameObject.AddComponent<UnityEngine.UI.Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.64f);
            var result = Panel("Result Card", overlay, new Vector2(0.035f, 0.17f), new Vector2(0.965f, 0.83f));
            manager.resultsText = Text("Trial Summary", result, "", 23f, Color.white);
            Fill(manager.resultsText.rectTransform, 26f);
            manager.resultsText.rectTransform.offsetMin = new Vector2(26f, 80f);
            manager.resultsText.enableAutoSizing = true;
            manager.resultsText.fontSizeMin = 15f;
            manager.resultsText.fontSizeMax = 25f;
            manager.resultsNext = Button(result, "Next Result", "Next terrain / combined", out _);
            manager.resultsClose = Button(result, "Close Results", "Close", out _);
            PlaceResultButton(manager.resultsNext, 0.05f, 0.47f);
            PlaceResultButton(manager.resultsClose, 0.53f, 0.95f);
            manager.resultsPanel = overlay.gameObject;
            manager.resultsPanel.SetActive(false);
        }

        private static void SectionLabel(Transform parent, string caption)
        {
            var heading = Text(caption, parent, caption, 16f, new Color(0.50f, 0.86f, 0.81f));
            heading.alignment = TextAlignmentOptions.MidlineLeft;
            heading.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 22f;
        }

        private static RectTransform ActionRow(Transform parent, string name, float height)
        {
            var row = Rect(name, parent);
            row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
            var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = true;
            return row;
        }

        private static TMP_InputField CrowdSizeField(Transform parent)
        {
            var rect = Rect("Crowd Count", parent);
            var image = RoundedImage(rect, new Color(0.10f, 0.19f, 0.24f), true);
            rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42f;
            var caption = Text("Caption", rect, "Walkers  ·  1–100", 18f, Color.white);
            Fill(caption.rectTransform, 10f);
            caption.alignment = TextAlignmentOptions.MidlineLeft;

            var viewport = Rect("Number Viewport", rect);
            viewport.anchorMin = new Vector2(0.63f, 0f);
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(2f, 4f);
            viewport.offsetMax = new Vector2(-8f, -4f);
            RoundedImage(viewport, new Color(0.035f, 0.095f, 0.12f), false);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var value = Text("Number", viewport, "1", 20f, new Color(0.67f, 1f, 0.94f));
            Fill(value.rectTransform, 7f);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.textWrappingMode = TextWrappingModes.NoWrap;

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = image;
            field.textViewport = viewport;
            field.textComponent = value;
            field.contentType = TMP_InputField.ContentType.Custom;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterValidation = TMP_InputField.CharacterValidation.Digit;
            field.characterLimit = 9;
            field.text = "1";
            return field;
        }

        private static void BuildSpeedSlider(Transform parent, StepIKSandbox manager)
        {
            var root = Rect("Walk Speed", parent);
            RoundedImage(root, new Color(0.10f, 0.19f, 0.24f), false);
            root.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 68f;
            manager.walkSpeedLabel = Text("Speed Value", root, "Walk speed   1.00 m/s", 18f, Color.white);
            manager.walkSpeedLabel.alignment = TextAlignmentOptions.MidlineLeft;
            var label = manager.walkSpeedLabel.rectTransform;
            label.anchorMin = new Vector2(0f, 0.44f);
            label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(10f, 0f);
            label.offsetMax = new Vector2(-10f, -2f);
            var area = Rect("Slider", root);
            area.anchorMax = new Vector2(1f, 0.46f);
            area.offsetMin = new Vector2(16f, 6f);
            area.offsetMax = new Vector2(-16f, -1f);
            var track = Rect("Track", area);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(0f, 6f);
            RoundedImage(track, new Color(0.035f, 0.095f, 0.12f), true);
            var fill = Rect("Fill", area);
            fill.anchorMin = new Vector2(0f, 0.5f);
            fill.anchorMax = new Vector2(1f, 0.5f);
            fill.sizeDelta = new Vector2(0f, 6f);
            RoundedImage(fill, new Color(0.28f, 0.80f, 0.66f), false);
            var handleArea = Rect("Handle Area", area);
            Fill(handleArea, 0f);
            var handle = Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(19f, 19f);
            var handleImage = RoundedImage(handle, new Color(0.77f, 1f, 0.93f), true);
            var slider = area.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0.5f;
            slider.maxValue = 2f;
            slider.value = 1f;
            manager.speedSlider = slider;
        }

        private static TMP_Dropdown Dropdown(Transform parent, string name)
        {
            var root = Rect(name + " Dropdown", parent);
            var image = RoundedImage(root, new Color(0.10f, 0.19f, 0.24f), true);
            root.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42f;
            var dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = image;
            var caption = Text("Value", root, name, 18f, Color.white);
            Fill(caption.rectTransform, 8f);
            caption.rectTransform.offsetMax = new Vector2(-29f, -4f);
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.captionText = caption;
            var arrow = Text("Arrow", root, "v", 18f, Color.white);
            arrow.alignment = TextAlignmentOptions.MidlineRight;
            Fill(arrow.rectTransform, 8f);

            var list = Rect("Template", root);
            list.anchorMin = new Vector2(0f, 0f);
            list.anchorMax = new Vector2(1f, 0f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = new Vector2(0f, 288f);
            list.anchoredPosition = Vector2.zero;
            RoundedImage(list, new Color(0.07f, 0.12f, 0.17f), true);
            var scroll = list.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", list);
            Fill(viewport, 3f);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 40f);
            var item = Rect("Item", content);
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.sizeDelta = new Vector2(0f, 40f);
            var itemImage = item.gameObject.AddComponent<UnityEngine.UI.Image>();
            itemImage.color = new Color(0.15f, 0.23f, 0.30f);
            var toggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = itemImage;
            var label = Text("Label", item, name, 18f, Color.white);
            Fill(label.rectTransform, 8f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            scroll.viewport = viewport;
            scroll.content = content;
            dropdown.template = list;
            dropdown.itemText = label;
            list.gameObject.SetActive(false);
            return dropdown;
        }

        private static void PlaceResultButton(UnityEngine.UI.Button button, float left, float right)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(left, 0f);
            rect.anchorMax = new Vector2(right, 0f);
            rect.offsetMin = new Vector2(0f, 16f);
            rect.offsetMax = new Vector2(0f, 63f);
        }

        private static void Validate(StepIKSandbox manager)
        {
            if (!manager.template.animator.avatar.isValid || !manager.template.animator.avatar.isHuman)
                throw new InvalidOperationException("Avatar validation failed.");
            if (!manager.template.leftLeg.IsValid || !manager.template.rightLeg.IsValid)
                throw new InvalidOperationException("Leg assignment validation failed.");
            if (UnityEngine.Object.FindObjectsByType<EventSystem>().Count(e => e.gameObject.scene == manager.gameObject.scene) != 1)
                throw new InvalidOperationException("Expected exactly one EventSystem.");
            Canvas.ForceUpdateCanvases();
            foreach (var button in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>().Where(b => b.gameObject.scene == manager.gameObject.scene))
                if (((RectTransform)button.transform).rect.height <= 0f) throw new InvalidOperationException("Zero-size button: " + button.name);
            StepIKMathChecks.RunInEditor();
        }
    }
}
