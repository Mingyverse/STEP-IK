using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StepIK.Editor
{
    public static class StepIKHumanoidSetupMenu
    {
        private const string SetupPath = "Tools/STEP-IK/Set Up Selected Humanoid";
        private const string ValidatePath = "Tools/STEP-IK/Validate Selected Humanoid";

        [MenuItem(SetupPath, true)]
        [MenuItem(ValidatePath, true)]
        private static bool CanUseMenu() => Selection.activeGameObject && !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(SetupPath)]
        private static void SetUp()
        {
            var animator = FindAnimator();
            if (!animator) return;
            if (animator.GetComponentInParent<StepIKController>(true) ||
                animator.GetComponentInChildren<StepIKController>(true))
            {
                Debug.LogWarning("STEP-IK already exists on this hierarchy. Use Validate Selected Humanoid to check the existing setup.", animator);
                return;
            }
            if (!animator.enabled || !animator.runtimeAnimatorController)
            {
                Debug.LogWarning("Enable the Animator and assign your existing Animator Controller before running STEP-IK setup.", animator);
                return;
            }
            float solePlaneY = animator.transform.position.y;
            if (!StepIKHumanoidCalibration.TryMeasure(animator, solePlaneY, out _, out _, out _, out var issue))
            {
                Debug.LogWarning("STEP-IK setup: " + issue, animator);
                return;
            }

            int groundLayer = LayerMask.NameToLayer("Ground");
            LayerMask mask = groundLayer >= 0 ? 1 << groundLayer : 1 << 0;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Up STEP-IK Humanoid");
            Undo.RecordObject(animator, "Configure STEP-IK Animator");
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var controller = Undo.AddComponent<StepIKController>(animator.gameObject);
            Undo.RecordObject(controller, "Calibrate STEP-IK Probes");
            if (!controller.TryBindHumanoid(animator, mask, solePlaneY, out issue))
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogWarning("STEP-IK setup: " + issue, animator);
                return;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            Undo.CollapseUndoOperations(group);

            Selection.activeGameObject = animator.gameObject;
            string layer = groundLayer >= 0 ? "Ground" : "Default";
            Debug.Log($"STEP-IK set up with the {layer} terrain layer. Check the sole probes in Scene view and confirm Terrain Mask matches your floor colliders.", controller);
            Validate(controller);
        }

        [MenuItem(ValidatePath)]
        private static void ValidateSelected()
        {
            var animator = FindAnimator();
            if (!animator) return;
            var controller = animator.GetComponentInParent<StepIKController>(true);
            if (controller) Validate(controller);
            else Debug.LogWarning("Add STEP-IK with Tools → STEP-IK → Set Up Selected Humanoid.", animator);
        }

        private static Animator FindAnimator()
        {
            var selected = Selection.activeGameObject;
            if (!selected || EditorUtility.IsPersistent(selected))
            {
                Debug.LogWarning("Select the character in the Hierarchy or open its prefab in Prefab Mode.");
                return null;
            }

            var ancestor = selected.GetComponentInParent<Animator>(true);
            if (ancestor) return ancestor;
            var animators = selected.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 1) return animators[0];
            Debug.LogWarning(animators.Length == 0
                ? "The selected hierarchy does not contain an Animator."
                : "Several Animators are in this hierarchy. Select the character's Animator GameObject directly.", selected);
            return null;
        }

        private static void Validate(StepIKController controller)
        {
            var warnings = new List<string>();
            var animator = controller.animator;
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                warnings.Add("Assign a valid Humanoid Animator.");
            else
            {
                if (!animator.enabled || !animator.runtimeAnimatorController)
                    warnings.Add("Enable the Animator and assign an Animator Controller.");
                if (animator.updateMode != AnimatorUpdateMode.Normal || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                    warnings.Add("Use Normal Animator updates and Always Animate culling.");
                if (controller.gameObject != animator.gameObject)
                    warnings.Add("Attach StepIKController to the Animator GameObject for callback support.");
            }

            if (!controller.pelvis || controller.leftLeg == null || controller.rightLeg == null ||
                !controller.leftLeg.IsValid || !controller.rightLeg.IsValid ||
                !controller.leftLeg.upperLeg.IsChildOf(controller.pelvis) ||
                !controller.rightLeg.upperLeg.IsChildOf(controller.pelvis) ||
                controller.leftLeg.upperLeg == controller.rightLeg.upperLeg ||
                controller.leftLeg.foot == controller.rightLeg.foot)
                warnings.Add("Assign the pelvis and valid, distinct left and right leg chains.");

            LayerMask terrain = controller.settings != null ? controller.settings.terrainMask : default;
            if (terrain.value == 0) warnings.Add("Choose a Terrain Mask containing the walkable surface layers.");
            else
            {
                foreach (var collider in controller.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger || (terrain.value & (1 << collider.gameObject.layer)) == 0) continue;
                    warnings.Add("A character collider shares a terrain layer. Put character colliders on a layer excluded from Terrain Mask.");
                    break;
                }
            }

            if (warnings.Count == 0)
                Debug.Log("STEP-IK references and Animator settings are configured. Test the calibrated probes on level ground before tuning slopes or stairs.", controller);
            else
                Debug.LogWarning("STEP-IK setup checks:\n• " + string.Join("\n• ", warnings), controller);
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void DrawFootProbes(StepIKController controller, GizmoType gizmoType)
        {
            var savedColor = Gizmos.color;
            DrawSole(controller.leftLeg);
            DrawSole(controller.rightLeg);
            Gizmos.color = savedColor;
        }

        private static void DrawSole(LegReferences leg)
        {
            if (leg == null || !leg.foot) return;
            Vector3 heel = leg.foot.TransformPoint(leg.heelOffset);
            Vector3 toe = leg.foot.TransformPoint(leg.toeOffset);
            float radius = Mathf.Clamp(Vector3.Distance(heel, toe) * 0.07f, 0.008f, 0.04f);
            Gizmos.color = new Color(0.17f, 0.89f, 0.77f);
            Gizmos.DrawLine(heel, toe);
            Gizmos.DrawWireSphere(heel, radius);
            Gizmos.color = new Color(1f, 0.73f, 0.29f);
            Gizmos.DrawWireSphere(toe, radius);
        }
    }
}
