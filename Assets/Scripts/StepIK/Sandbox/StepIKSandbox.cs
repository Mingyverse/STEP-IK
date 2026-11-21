using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace StepIK.Sandbox
{
    [DefaultExecutionOrder(300)]
    public sealed class StepIKSandbox : MonoBehaviour
    {
        public StepIKController template;
        public StepIKController[] cast;
        public TMP_Dropdown methodChoice, worldChoice, countChoice, speedChoice, cameraChoice, castChoice;
        public TMP_InputField crowdInput;
        public UnityEngine.UI.Slider speedSlider;
        public TMP_Text walkSpeedLabel, sprintLabel;
        public UnityEngine.UI.Button sprintButton;
        public TMP_Dropdown benchmarkChoice;
        public bool useRoughWorlds;
        public GameObject[] terrainRoots;
        public Camera viewCamera;
        public TMP_Text methodLabel, terrainLabel, countLabel, speedLabel, cameraLabel, statusLabel, metricsLabel, noteLabel;
        public UnityEngine.UI.Button methodButton, terrainButton, countButton, speedButton, cameraButton;
        public UnityEngine.UI.Button playButton, resetButton, runButton, suiteButton, exportButton;
        public UnityEngine.UI.Button manualButton, matrixButton, resultsNext, resultsClose;
        public TMP_Text manualLabel, resultsText;
        public GameObject resultsPanel;
        public float walkClipSeconds = 1f, idleClipSeconds = 5f;
        public AnimationCurve leftStance, rightStance;
        public bool authoredCharacter;
        public string generatedAssetFolder;
        [Min(1f)] public float warmupSeconds = 1f;
        [Min(1f)] public float measurementSeconds = 6f;

        private static readonly (string Label, TestMode Mode, bool Adaptive)[] ComparisonMethods =
        {
            ("FK only", TestMode.FKOnly, false),
            ("Unity Animator IK / single ray", TestMode.UnityAnimatorIK, false),
            ("Single ray / Two-Bone", TestMode.SingleRayIK, false),
            ("CCD / single ray", TestMode.CcdIK, false),
            ("FABRIK / single ray", TestMode.FabrikIK, false),
            ("STEP-IK / fixed", TestMode.StepIK, false),
            ("STEP-IK / adaptive", TestMode.StepIK, true)
        };
        public static readonly string[] Methods = ComparisonMethods.Select(option => option.Label).ToArray();
        public static readonly string[] Terrains = { "Empty world", "Slopes", "Stairs", "Uneven ground", "Meadow", "Rock Quarry", "Red Badlands", "Frost Basin", "Mosswood" };
        private const int MaxActors = 100;
        private static readonly int[] BenchmarkWorlds = { 1, 2, 3 };
        private static readonly int[] StressCounts = { 50, 100 };
        private readonly int[] counts = { 1, 4, 8, 16, 25, 50, 100 };
        private readonly float[] speeds = { 1f, 0.75f, 1.25f };
        private float sliderSpeed = 1f;
        private readonly StepIKController[] actors = new StepIKController[MaxActors];
        private readonly CrowdProfile[] profiles = new CrowdProfile[MaxActors];
        private readonly BenchmarkMetrics metrics = new BenchmarkMetrics(MaxActors);
        private readonly List<BenchmarkFrame> frames = new List<BenchmarkFrame>(8192);
        private static readonly ProfilerMarker MeasureMarker = new ProfilerMarker("StepIK.MeasurementOverhead");
        private int method, terrain, activeCount = 1, speedIndex, cameraMode;
        private int castIndex, matrixIndex;
        private int stressIndex, countBeforeBenchmark;
        private bool stressBenchmark, benchmarkLostFocus;
        private float yaw, pitch = 16f;
        private bool dragging;
        private string runFolder, castDescription;
        private float clock, nextUi, travelDistance;
        private bool playing = true, recording, suite, measuring;
        private bool manual, matrix;
        private float idleTime, manualSpeed;
        private float manualPhase;
        private bool sprintRequested;
        private readonly ManualSprintState sprint = new ManualSprintState();
        private const float SprintMultiplier = 2f;
        private const float RunReferenceSpeed = 2.5f;
        private const float TurnDegreesPerSecond = 420f;
        private const float ManualAcceleration = 6f;
        private int resultPage;
        private readonly List<TrialSummary> summaries = new List<TrialSummary>();
        private System.Diagnostics.Process process;
        private double cpuStart;
        private double measurementStart;
        private long peakWorkingSet;
        private float nextMemory;
        private int WorldCount => Mathf.Min(Terrains.Length, terrainRoots.Length);
        private string status = "Preview - results are not recorded", lastExport;
        private BenchmarkFrame latest;
        public int ActiveCount => activeCount;
        public float WalkSpeed => speedSlider ? sliderSpeed : speeds[speedIndex];
        public bool IsSprinting => manual && sprint.Active;
        public float ManualSpeed => manualSpeed;
        private bool UsesNativeIK => ComparisonMethods[method].Mode == TestMode.UnityAnimatorIK;

        private void Start()
        {
            if (!template || !template.animator || !template.animator.isHuman)
            { Debug.LogError("Sandbox needs a valid Humanoid template.", this); enabled = false; return; }
            process = System.Diagnostics.Process.GetCurrentProcess();
            if (methodChoice)
            {
                methodChoice.ClearOptions();
                Bind(methodChoice, Methods, ChangeMethod);
                Bind(worldChoice, Terrains.Take(WorldCount), value => { terrain = value; ResetTrial(); });
                if (crowdInput) crowdInput.onEndEdit.AddListener(CommitCrowdCount);
                else if (countChoice)
                    Bind(countChoice, counts.Select(n => $"Characters · {n}"), value => SetCrowdCount(counts[value]));
                if (speedSlider)
                {
                    sliderSpeed = speedSlider.value;
                    speedSlider.onValueChanged.AddListener(value =>
                    {
                        if (recording) return;
                        sliderSpeed = Mathf.Round(value * 100f) / 100f;
                        RefreshUI();
                    });
                }
                else Bind(speedChoice, speeds.Select(n => $"Speed · {n:F2} m/s"), value => { speedIndex = value; ResetTrial(); });
                Bind(cameraChoice, new[] { "Camera · Follow", "Camera · Foot close-up", "Camera · Overview" }, value => cameraMode = value);
                Bind(castChoice, new[] { "Cast · Mixed" }.Concat(cast.Select(a => a.name)), value =>
                {
                    castIndex = value;
                    for (int i = 0; i < actors.Length; i++)
                    {
                        if (!actors[i]) continue;
                        actors[i].gameObject.SetActive(false);
                        Destroy(actors[i].gameObject);
                        actors[i] = null;
                    }
                    ResetTrial();
                });
                if (benchmarkChoice) Bind(benchmarkChoice, new[] { "Benchmark · Selected count", "Benchmark · 50 + 100" }, _ => { });
            }
            else
            {
                actors[0] = template;
                methodButton.onClick.AddListener(() => ChangeMethod((method + 1) % Methods.Length));
                terrainButton.onClick.AddListener(() => { if (!recording) { terrain = (terrain + 1) % WorldCount; ResetTrial(); } });
                countButton.onClick.AddListener(() => SetCrowdCount(counts[(Array.IndexOf(counts, ActiveCount) + 1) % counts.Length]));
                speedButton.onClick.AddListener(() => { if (!recording) { speedIndex = (speedIndex + 1) % speeds.Length; ResetTrial(); } });
                cameraButton.onClick.AddListener(() => { if (!recording) cameraMode = (cameraMode + 1) % 3; });
            }
            playButton.onClick.AddListener(() =>
            {
                if (recording) return;
                playing = !playing;
                if (!playing) { sprint.Reset(); sprintRequested = false; manualSpeed = 0f; }
                status = playing ? "Preview - not recording" : "Preview paused";
            });
            resetButton.onClick.AddListener(() => ResetPreview("Preview reset"));
            runButton.onClick.AddListener(() => BeginRun(false));
            suiteButton.onClick.AddListener(() => BeginRun(true));
            if (manualButton) manualButton.onClick.AddListener(ToggleManual);
            if (sprintButton) sprintButton.onClick.AddListener(() => { if (manual && playing && !recording) sprintRequested = true; });
            if (matrixButton) matrixButton.onClick.AddListener(() => BeginRun(true, true));
            if (resultsNext) resultsNext.onClick.AddListener(NextResultPage);
            if (resultsClose) resultsClose.onClick.AddListener(() => resultsPanel.SetActive(false));
            exportButton.onClick.AddListener(() => { if (!recording && !string.IsNullOrEmpty(lastExport)) Application.OpenURL("file:///" + Path.GetDirectoryName(lastExport).Replace('\\', '/')); });
            ResetTrial();
        }

        private void Bind(TMP_Dropdown dropdown, IEnumerable<string> labels, Action<int> changed)
        {
            dropdown.AddOptions(labels.ToList());
            dropdown.onValueChanged.AddListener(value => { if (!recording) changed(value); });
        }

        private void CommitCrowdCount(string input)
        {
            SetCrowdCount(ParseCrowdCount(input, ActiveCount));
            SyncCrowdInput();
        }

        public static int ParseCrowdCount(string input, int fallback)
        {
            if (string.IsNullOrEmpty(input) || input.Any(character => character < '0' || character > '9'))
                return Mathf.Clamp(fallback, 1, MaxActors);
            if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return MaxActors;
            return Mathf.Clamp(value, 1, MaxActors);
        }

        private void SetCrowdCount(int value)
        {
            if (recording || manual || value == activeCount) return;
            activeCount = value;
            ResetTrial();
        }

        private void SyncCrowdInput()
        {
            if (!crowdInput || crowdInput.isFocused) return;
            string value = (manual ? 1 : ActiveCount).ToString(CultureInfo.InvariantCulture);
            if (crowdInput.text != value) crowdInput.SetTextWithoutNotify(value);
        }

        private void ChangeMethod(int value)
        {
            if (recording) return;
            method = value;
            if (manual) { SetActorMethod(actors[0]); metrics.Reset(); }
            else ResetTrial();
        }

        private void EnsureActor(int index)
        {
            if (actors[index]) return;
            var source = !methodChoice || cast.Length == 0 ? template :
                cast[castIndex > 0 ? castIndex - 1 : index % cast.Length];
            var actor = Instantiate(source.gameObject, source.transform.parent).GetComponent<StepIKController>();
            actor.name = source.name + " " + (index + 1);
            actors[index] = actor;
            profiles[index] = actor.GetComponent<CrowdProfile>();
        }

        private void OnDestroy() => process?.Dispose();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && recording && !Application.isEditor)
                benchmarkLostFocus = true;
        }

        private void ResetPreview(string message)
        {
            if (recording && stressBenchmark) activeCount = countBeforeBenchmark;
            recording = suite = matrix = stressBenchmark = benchmarkLostFocus = false;
            playing = true;
            status = message;
            ResetTrial();
        }

        private void ToggleManual()
        {
            if (recording) return;
            manual = !manual;
            playing = true;
            status = manual ? "WASD / arrows to move · tap Shift to sprint · release movement to reset · drag to orbit" : "Automatic preview";
            ResetTrial();
        }

        private void ResetTrial()
        {
            clock = idleTime = manualSpeed = travelDistance = 0f;
            manualPhase = 0f;
            sprint.Reset();
            sprintRequested = false;
            measuring = false; frames.Clear(); metrics.Reset(); latest = default;
            for (int t = 0; t < terrainRoots.Length; t++) terrainRoots[t].SetActive(t == terrain);
            viewCamera.backgroundColor = terrain >= 4 ? new Color(0.46f, 0.65f, 0.77f) : new Color(0.10f, 0.16f, 0.23f);
            for (int i = 0; i < actors.Length; i++)
            {
                bool active = i < (manual ? 1 : ActiveCount);
                if (active) EnsureActor(i);
                var actor = actors[i];
                if (!actor) continue;
                actor.EndSimulation();
                actor.gameObject.SetActive(active);
                SetActorMethod(actor);
                if (!actor.gameObject.activeSelf) continue;
                actor.animator.Rebind(); actor.animator.speed = 0f;
                PositionActor(actor, i);
                actor.animator.Play("Walk", 0, 0f);
                actor.StartSimulation();
            }
            Physics.SyncTransforms();
            RefreshUI();
        }

        private void SetActorMethod(StepIKController actor)
        {
            var choice = ComparisonMethods[method];
            actor.adaptiveUpdates = choice.Adaptive;
            actor.SetMode(choice.Mode);
        }

        private void BeginRun(bool all, bool allWorlds = false)
        {
            if (recording) return;
            summaries.Clear();
            stressBenchmark = allWorlds && benchmarkChoice && benchmarkChoice.value == 1;
            countBeforeBenchmark = activeCount;
            stressIndex = 0;
            if (stressBenchmark) activeCount = StressCounts[stressIndex];
            for (int i = ActiveCount; i < actors.Length; i++)
            {
                if (!actors[i]) continue;
                actors[i].gameObject.SetActive(false);
                Destroy(actors[i].gameObject);
                actors[i] = null;
            }
            runFolder = Path.Combine(Application.persistentDataPath, "StepIKBenchmarks",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + (stressBenchmark ? "_N50_N100" : "_N" + ActiveCount));
            castDescription = !methodChoice ? template.name : castIndex > 0 ? cast[castIndex - 1].name :
                string.Join(" | ", cast.Take(ActiveCount).Select(a => a.name));
            manual = false;
            matrix = allWorlds;
            matrixIndex = 0;
            if (matrix) terrain = BenchmarkWorlds[0];
            suite = all; if (all) method = 0;
            recording = true; playing = true; ResetTrial();
            status = "Warming up... configuration locked";
        }

        private void Update()
        {
            if (!actors[0]) return;
            if (benchmarkLostFocus)
            {
                ResetPreview("Recording cancelled because the window lost focus. Keep STEP-IK focused and restart the benchmark.");
                return;
            }
            UpdateOrbitInput();
            if (playing)
            {
                clock += Time.deltaTime;
                if (!manual) travelDistance += WalkSpeed * Time.deltaTime;
            }
            if (manual)
            {
                UpdateManual();
                return;
            }
            for (int i = 0; i < ActiveCount; i++)
            {
                var actor = actors[i];
                actor.isRunning = playing;
                PositionActor(actor, i);
                // Same explicit clip time and speed for all methods. No solver owns locomotion.
                ApplyWalk(actor, i, travelDistance + (i % 10) * 0.093f);
            }
        }

        private void ApplyWalk(StepIKController actor, int index, float distance)
        {
            var profile = profiles[index];
            float phase = Mathf.Repeat(distance / Mathf.Max(0.01f, profile ? profile.walkSeconds : walkClipSeconds), 1f);
            actor.animator.Play("Walk", 0, phase);
            if (profile || authoredCharacter)
            {
                actor.leftLeg.contactWeight = Mathf.Clamp01((profile ? profile.leftStance : leftStance).Evaluate(phase));
                actor.rightLeg.contactWeight = Mathf.Clamp01((profile ? profile.rightStance : rightStance).Evaluate(phase));
                return;
            }
            float swing = Mathf.Sin(phase * Mathf.PI * 2f);
            actor.leftLeg.contactWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(swing * 5f));
            actor.rightLeg.contactWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-swing * 5f));
        }

        private void UpdateManual()
        {
            var actor = actors[0];
            actor.isRunning = playing;
            if (!playing) return;
            var keys = Keyboard.current;
            Vector3 direction = Vector3.zero;
            if (keys != null)
            {
                if (!crowdInput || !crowdInput.isFocused)
                {
                    if (keys.wKey.isPressed || keys.upArrowKey.isPressed) direction.z++;
                    if (keys.sKey.isPressed || keys.downArrowKey.isPressed) direction.z--;
                    if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) direction.x++;
                    if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) direction.x--;
                }
            }
            bool moving = direction.sqrMagnitude > 0f;
            bool shiftPressed = keys != null && (keys.leftShiftKey.wasPressedThisFrame || keys.rightShiftKey.wasPressedThisFrame);
            bool shiftHeld = keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
            sprint.Step(moving, shiftPressed || sprintRequested, shiftHeld);
            sprintRequested = false;
            float desiredSpeed = WalkSpeed * (sprint.Active ? SprintMultiplier : 1f);
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            direction = forward * direction.z + right * direction.x;
            const float minMotion = 0.01f;
            var body = actor.transform;
            float delta = Time.deltaTime;
            float targetSpeed = 0f;
            if (direction.sqrMagnitude > 0f)
            {
                direction.Normalize();
                var desiredRotation = Quaternion.LookRotation(direction);
                body.rotation = Quaternion.RotateTowards(body.rotation, desiredRotation, TurnDegreesPerSecond * delta);
                // Slow down on tight turns so the character follows its facing instead of sliding sideways.
                targetSpeed = desiredSpeed * Mathf.Clamp01(Vector3.Dot(body.forward, direction));
            }
            manualSpeed = Mathf.MoveTowards(manualSpeed, targetSpeed, ManualAcceleration * delta);
            float advanced = 0f;
            if (manualSpeed >= minMotion)
            {
                Vector3 previous = body.position;
                Vector3 destination = previous + body.forward * (manualSpeed * delta);
                destination.x = Mathf.Clamp(destination.x, -29f, 29f);
                destination.z = Mathf.Clamp(destination.z, -11f, 35f);
                if (Physics.Raycast(new Vector3(destination.x, 5f, destination.z), Vector3.down, out var ground, 10f, actor.settings.terrainMask, QueryTriggerInteraction.Ignore))
                {
                    destination.y = ground.point.y;
                    body.position = destination;
                    advanced = new Vector2(destination.x - previous.x, destination.z - previous.z).magnitude;
                }
            }
            if (advanced > 0.00001f)
            {
                var profile = profiles[0];
                bool running = profile && profile.runSeconds > 0f && manualSpeed > WalkSpeed * 1.12f;
                float seconds = running ? profile.runSeconds : profile ? profile.walkSeconds : walkClipSeconds;
                float referenceSpeed = running ? RunReferenceSpeed : 1f;
                manualPhase = Mathf.Repeat(manualPhase + advanced / Mathf.Max(0.01f, seconds * referenceSpeed), 1f);
                if (running)
                {
                    actor.animator.Play("Run", 0, manualPhase);
                    actor.SetContactWeights(profile.leftRunStance.Evaluate(manualPhase), profile.rightRunStance.Evaluate(manualPhase));
                }
                else
                {
                    // Preserve phase when changing gait or walk speed.
                    ApplyWalk(actor, 0, manualPhase * seconds);
                }
            }
            else if (authoredCharacter)
            {
                idleTime += Time.deltaTime;
                actor.animator.Play("Idle", 0, Mathf.Repeat(idleTime / (profiles[0] ? profiles[0].idleSeconds : idleClipSeconds), 1f));
                actor.leftLeg.contactWeight = actor.rightLeg.contactWeight = 1f;
            }
        }

        private void PositionActor(StepIKController actor, int index)
        {
            int columns = manual ? 1 : Mathf.Min(50, ActiveCount);
            float x = (index % columns - (columns - 1) * 0.5f) * 0.69f;
            // Start measurement before the terrain features and avoid a loop reset during capture.
            float offset = -(index / columns) * 0.72f;
            float z = recording
                ? RecordedRouteStartZ(terrain) + (clock - warmupSeconds) * WalkSpeed + offset
                : 1f + Mathf.Repeat(travelDistance, 16f) + offset;
            z = Mathf.Clamp(z, -11f, 35f);
            float y = useRoughWorlds ? SandboxTerrain.MeshHeight(terrain, x, z) : SandboxTerrain.LegacyHeight(terrain, x, z);
            actor.transform.position = new Vector3(x, y, z);
            actor.transform.rotation = Quaternion.identity;
        }

        private static float RecordedRouteStartZ(int terrainIndex)
        {
            if (terrainIndex == 3) return 5f;
            if (terrainIndex == 1 || terrainIndex == 2) return 2.6f;
            return 0f;
        }

        private void UpdateOrbitInput()
        {
            var mouse = Mouse.current;
            if (recording || mouse == null || (resultsPanel && resultsPanel.activeSelf))
            {
                dragging = false;
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                dragging = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) dragging = false;
            if (!dragging) return;
            Vector2 delta = mouse.delta.ReadValue();
            yaw = Mathf.Repeat(yaw + delta.x * 0.24f, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * 0.20f, 5f, 75f);
        }

        public static float SurfaceHeight(int type, float x, float z)
        {
            return SandboxTerrain.HeightAt(type, x, z);
        }

        private void LateUpdate()
        {
            if (!actors[0]) return;
            if (playing)
            {
                if (recording && !measuring && clock >= warmupSeconds)
                {
                    measuring = true; metrics.Reset(); frames.Clear();
                    cpuStart = process.TotalProcessorTime.TotalSeconds;
                    measurementStart = Time.realtimeSinceStartupAsDouble;
                    peakWorkingSet = 0; nextMemory = 0f;
                }
                if (recording && measuring && clock >= nextMemory)
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, ProcessMemory.WorkingSet(process));
                    nextMemory = clock + 0.5f;
                }
                using (MeasureMarker.Auto()) latest = metrics.Measure(actors, manual ? 1 : ActiveCount,
                    clock - warmupSeconds, Time.unscaledDeltaTime);
                if (recording && measuring && clock <= warmupSeconds + measurementSeconds) frames.Add(latest);
                if (recording)
                {
                    int total = BenchmarkWorlds.Length * Methods.Length * (stressBenchmark ? StressCounts.Length : 1);
                    string progress = matrix ? $"Trial {summaries.Count + 1}/{total} · {ActiveCount} walkers · " : "";
                    status = clock < warmupSeconds
                        ? $"{progress}{Terrains[terrain]} · {Methods[method]} · Warming up"
                        : $"{progress}{Terrains[terrain]} · {Methods[method]} · {clock - warmupSeconds:F1} / {measurementSeconds:F0}s";
                    if (clock >= warmupSeconds + measurementSeconds) FinishRun();
                }
            }
            UpdateCamera();
            if (Time.unscaledTime >= nextUi) { nextUi = Time.unscaledTime + 0.15f; RefreshUI(); }
        }

        private void FinishRun()
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - measurementStart;
            double cpu = elapsed > 0d ? 100d * (process.TotalProcessorTime.TotalSeconds - cpuStart) / (elapsed * Environment.ProcessorCount) : double.NaN;
            var summary = new TrialSummary(Terrains[terrain], Methods[method], frames, cpu, peakWorkingSet);
            summary.agentCount = ActiveCount;
            int builtinCallbacksSeen = UsesNativeIK ? actors.Take(ActiveCount).Count(a => a.BuiltInCallbackSeen) : 0;
            if (UsesNativeIK && builtinCallbacksSeen != ActiveCount)
                summary.score = double.NaN;
            summaries.Add(summary);
            try { Export(summary, elapsed, builtinCallbacksSeen); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                status = "Export failed: " + e.Message;
                recording = suite = matrix = stressBenchmark = playing = false;
                ShowResults();
                return;
            }
            if (suite && method < Methods.Length - 1) { method++; ResetTrial(); return; }
            if (matrix && matrixIndex + 1 < BenchmarkWorlds.Length)
            {
                terrain = BenchmarkWorlds[++matrixIndex];
                method = 0;
                ResetTrial();
                return;
            }
            if (stressBenchmark && stressIndex + 1 < StressCounts.Length)
            {
                activeCount = StressCounts[++stressIndex];
                matrixIndex = method = 0;
                terrain = BenchmarkWorlds[0];
                ResetTrial();
                return;
            }
            recording = suite = matrix = stressBenchmark = false;
            playing = false;
            try
            {
                string summaryPath = Path.Combine(runFolder, "Summary.csv");
                File.WriteAllText(summaryPath, BenchmarkScoring.Csv(BenchmarkScoring.Aggregate(summaries).Concat(summaries)));
                File.WriteAllText(Path.Combine(runFolder, "Scoring.txt"), BenchmarkScoring.Formula);
                lastExport = summaryPath;
                status = "Run complete. Summary.csv and per-trial files saved.";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { status = "Summary export failed: " + error.Message; }
            ShowResults();
        }

        private void ShowResults()
        {
            if (!resultsPanel) return;
            resultPage = 0;
            ShowResultPage();
            resultsPanel.SetActive(true);
        }

        private void ShowResultPage()
        {
            var pages = ResultPages();
            var page = pages[resultPage];
            var trials = summaries.Where(t => t.agentCount == page.count);
            IEnumerable<TrialSummary> rows = page.world == "Combined" ? BenchmarkScoring.Aggregate(trials) :
                trials.Where(x => x.world == page.world).OrderByDescending(x => double.IsNaN(x.score) ? -1 : x.score);
            var table = new StringBuilder($"<size=125%><b>{page.world} results</b></size>   {page.count} walkers · {WalkSpeed:F2} m/s · {resultPage + 1}/{pages.Length}\n\n");
            table.AppendLine("<b>Method<pos=31%>Score<pos=40%>Gap mm<pos=50%>Clip %<pos=59%>°/s<pos=66%>Frame ms<pos=77%>IK ms<pos=85%>CPU %<pos=93%>MiB</b>");
            foreach (var r in rows)
                table.AppendLine($"{r.method}<pos=31%>{Value(r.score)}<pos=40%>{Value(r.meanStanceGapMm)}<pos=50%>{Value(r.clipPercent)}<pos=59%>{Value(r.meanCorrectionDegreesPerSecond)}<pos=66%>{Value(r.meanFrameMs)}<pos=77%>{Value(r.meanScriptMs, "F3")}<pos=85%>{Value(r.processCpuPercent)}<pos=93%>{Value(r.peakWorkingSetMiB, "F0")}");
            table.AppendLine("\n<size=82%>Higher score is better. Foot contact and clipping account for 60% of the score. Gap is measured during stance.\nManaged IK time excludes Unity's native solve. Frame and process CPU measurements include its effects.\nCombined scores average the terrain scores. RAM shows the highest sampled working set.</size>");
            resultsText.text = table.ToString();
            resultsNext.interactable = pages.Length > 1;
        }

        private (int count, string world)[] ResultPages() => summaries.GroupBy(t => t.agentCount)
            .SelectMany(g => new[] { "Combined" }.Concat(g.Select(t => t.world).Distinct())
                .Select(world => (g.Key, world))).ToArray();

        private void NextResultPage()
        {
            resultPage = (resultPage + 1) % ResultPages().Length;
            ShowResultPage();
        }

        private static string Value(double value, string format = "F1") =>
            double.IsNaN(value) || double.IsInfinity(value) ? "—" : value.ToString(format);

        private void UpdateCamera()
        {
            Vector3 focus = actors[0].transform.position + Vector3.up * (cameraMode == 1 ? 0.28f : 0.90f);
            if (!manual && ActiveCount > 1) focus = Vector3.Lerp(focus, actors[ActiveCount - 1].transform.position + Vector3.up * 0.9f, 0.5f);
            float distance = cameraMode == 1 ? 2.1f : cameraMode == 2 || (!manual && ActiveCount > 25)
                ? Mathf.Max(15f, Mathf.Min(50, ActiveCount) * 0.66f)
                : !manual && ActiveCount > 8 ? 9f : 4.4f;
            Quaternion rotation = Quaternion.Euler(cameraMode == 2 ? Mathf.Max(35f, pitch) : pitch, yaw, 0f);
            viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        private void RefreshUI()
        {
            if (methodChoice)
            {
                methodChoice.SetValueWithoutNotify(method);
                worldChoice.SetValueWithoutNotify(terrain);
                methodChoice.interactable = worldChoice.interactable =
                    cameraChoice.interactable = castChoice.interactable = !recording;
                if (speedSlider)
                {
                    speedSlider.interactable = !recording;
                    walkSpeedLabel.text = $"Walk speed   {WalkSpeed:F2} m/s";
                }
                else speedChoice.interactable = !recording;
                if (crowdInput)
                {
                    crowdInput.interactable = !recording && !manual;
                    SyncCrowdInput();
                }
                else if (countChoice)
                {
                    countChoice.interactable = !recording && !manual;
                    countChoice.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(counts, ActiveCount)));
                }
                if (benchmarkChoice) benchmarkChoice.interactable = !recording;
            }
            else
            {
            methodLabel.text = Methods[method]; terrainLabel.text = "World · " + Terrains[terrain];
            countLabel.text = "Characters · " + (manual ? 1 : ActiveCount); speedLabel.text = $"Speed · {WalkSpeed:F2} m/s";
            cameraLabel.text = "Camera · " + new[] { "Follow", "Foot close-up", "Overview" }[cameraMode];
            methodButton.interactable = terrainButton.interactable = countButton.interactable = speedButton.interactable = cameraButton.interactable = !recording;
            countButton.interactable = !recording && !manual;
            }
            runButton.interactable = suiteButton.interactable = playButton.interactable = !recording;
            if (manualButton) manualButton.interactable = !recording;
            if (matrixButton) matrixButton.interactable = !recording;
            if (manualLabel) manualLabel.text = manual ? "Auto preview" : sprintButton ? "Manual walk" : "Manual walk (WASD / arrows)";
            if (sprintButton)
            {
                sprintButton.interactable = manual && playing && !recording;
                sprintLabel.text = sprint.Active ? $"Sprint on · {WalkSpeed * SprintMultiplier:F1}" : "Sprint · Shift";
            }
            exportButton.interactable = !recording && !string.IsNullOrEmpty(lastExport);
            statusLabel.text = status;
            string stanceGap = latest.stanceProbes > 0 ? $"{latest.stanceMeanAbsoluteGap * 1000f:F1} mm" : "—";
            metricsLabel.text = $"LIVE DIAGNOSTICS\nFrame interval   {latest.frameMs:F2} ms\nEstimated FPS   {(latest.frameMs > 0f ? 1000f / latest.frameMs : 0f):F1}\nManaged IK   {latest.scriptMs:F3} ms\nGround queries   {latest.groundQueries}\nCached feet   {latest.cachedFeet}\nStance gap   {stanceGap}\nClipped feet / frame   {latest.clippedFootFrames}\nNew clipping events   {latest.clippingEvents}\nCorrection change   {latest.correctionDegreesPerSecond:F1} deg/s\nValid sole probes   {latest.validProbes}";
            noteLabel.text = manual
                ? "WASD / arrows move relative to the camera. Tap Shift while moving to sprint. Release movement to reset. Drag in the view to orbit."
                : UsesNativeIK
                ? "Built-in IK: script time EXCLUDES Unity's native solve. Use Profiler / matched total frame measurements."
                : "Stance gap uses grounded phases of the shared clip. Correction change measures foot rotation relative to the base pose.";
            if (UsesNativeIK && clock > 1f && !actors[0].BuiltInCallbackSeen)
                statusLabel.text = "Built-in IK callback missing: check Humanoid avatar and layer IK Pass.";
        }

        [Serializable]
        private sealed class RunMetadata
        {
            public int schemaVersion = 2;
            public string method, terrain, camera, unityVersion, device, processor, graphics, operatingSystem, assets, scene;
            public string animation;
            public string timing = "Managed IK time excludes Unity's native solve. Frame intervals include rendering, scene instrumentation, UI and Editor overhead in Play mode.";
            public string sampling = "One row per measurement frame. Gap averages weight valid probes. Clipping episodes count entries into clipping among observed foot frames. Four independent ground probes per actor and frame are outside the algorithm query count. Missing numeric values are blank in CSV and null in JSON.";
            public string scope;
            public int agents, frames, vSyncCount, targetFrameRate, screenWidth, screenHeight, fabrikIterations, ccdIterations, logicalProcessors;
            public float speed, warmupSeconds, measurementSeconds, clippingThreshold;
            public int builtinCallbacksExpected, builtinCallbacksSeen;
            public bool editor, builtinCallbackSeen, roughWorlds;
            public IKSettings settings;
            public TrialSummary summary;
            public string cast, scoring = BenchmarkScoring.Formula;
            public string[] activeActors;
            public float cameraYaw, cameraPitch, routeStartZ;
            public double observedMeasurementSeconds, probeHitPercent;
        }

        private void Export(TrialSummary summary, double observedMeasurementSeconds, int builtinCallbacksSeen)
        {
            var invariant = CultureInfo.InvariantCulture;
            string folder = runFolder;
            Directory.CreateDirectory(folder);
            string stem = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + method + "_" + terrain + "_N" + ActiveCount;
            var csv = new StringBuilder("time_s,frame_ms,script_ms,valid_probes,stance_probes,mean_abs_gap_m,stance_mean_abs_gap_m,clipped_foot_frames,new_clipping_events,correction_deg_per_s,algorithm_ground_queries,cached_feet,observed_feet\n");
            foreach (var f in frames)
                csv.AppendFormat(invariant, "{0:F6},{1:F6},{2:F6},{3},{4},{5},{6},{7},{8},{9:F6},{10},{11},{12}\n",
                    f.time, f.frameMs, f.scriptMs, f.validProbes, f.stanceProbes,
                    BenchmarkExport.CsvNumber(f.meanAbsoluteGap, "F6"), BenchmarkExport.CsvNumber(f.stanceMeanAbsoluteGap, "F6"),
                    f.clippedFootFrames, f.clippingEvents, f.correctionDegreesPerSecond, f.groundQueries, f.cachedFeet, f.observedFeet);
            string csvPath = Path.Combine(folder, stem + ".csv");
            File.WriteAllText(csvPath, csv.ToString());
            var metadata = new RunMetadata { method = Methods[method], terrain = Terrains[terrain], camera = new[] { "Follow", "Foot close-up", "Overview" }[cameraMode],
                unityVersion = Application.unityVersion, device = SystemInfo.deviceModel, processor = SystemInfo.processorType,
                graphics = SystemInfo.graphicsDeviceName, operatingSystem = SystemInfo.operatingSystem, assets = generatedAssetFolder,
                scene = gameObject.scene.path,
                agents = ActiveCount, frames = frames.Count, vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate, logicalProcessors = Environment.ProcessorCount,
                speed = WalkSpeed, warmupSeconds = warmupSeconds,
                measurementSeconds = measurementSeconds, clippingThreshold = BenchmarkMetrics.ClipThreshold,
                screenWidth = Screen.width, screenHeight = Screen.height, editor = Application.isEditor,
                builtinCallbackSeen = UsesNativeIK && builtinCallbacksSeen == ActiveCount,
                builtinCallbacksExpected = UsesNativeIK ? ActiveCount : 0, builtinCallbacksSeen = builtinCallbacksSeen,
                fabrikIterations = actors[0].fabrikIterations, ccdIterations = actors[0].ccdIterations, settings = actors[0].settings,
                cast = castDescription,
                activeActors = actors.Take(ActiveCount).Select(actor => actor.name).ToArray(),
                cameraYaw = yaw, cameraPitch = pitch,
                routeStartZ = RecordedRouteStartZ(terrain), observedMeasurementSeconds = observedMeasurementSeconds,
                roughWorlds = useRoughWorlds, probeHitPercent = summary.ProbeHitPercent,
                animation = authoredCharacter ? "City People basic walk; shared explicit animation phase" : "Prototype muscle walk",
                scope = "Static terrain and matched scripted route. CPU percentage and working set describe the Unity process.",
                summary = summary };
            File.WriteAllText(Path.Combine(folder, stem + ".json"),
                BenchmarkExport.NormalizeMissingJsonNumbers(JsonUtility.ToJson(metadata, true)));
            lastExport = csvPath;
            Debug.Log("STEP-IK run exported: " + lastExport, this);
        }
    }
}
