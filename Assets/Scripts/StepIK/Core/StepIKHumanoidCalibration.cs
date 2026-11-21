using UnityEngine;

namespace StepIK
{
    /// <summary>Estimates starting sole probes from an upright Humanoid's neutral pose.</summary>
    public static class StepIKHumanoidCalibration
    {
        public static bool TryMeasure(Animator source, float solePlaneY, out Transform pelvis,
            out LegReferences left, out LegReferences right, out string error)
        {
            pelvis = null;
            left = right = null;
            if (!source || !source.avatar || !source.avatar.isValid || !source.avatar.isHuman)
            {
                error = "Assign a valid Humanoid Avatar to the Animator.";
                return false;
            }
            if (float.IsNaN(solePlaneY) || float.IsInfinity(solePlaneY))
            {
                error = "The neutral sole plane height must be finite.";
                return false;
            }

            Vector3 scale = source.transform.lossyScale;
            float largest = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            float smallest = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            if (smallest <= 0f || largest - smallest > largest * 0.01f ||
                Vector3.Dot(source.transform.up, Vector3.up) < 0.999f)
            {
                error = "Use an upright character with positive uniform world scale.";
                return false;
            }

            pelvis = source.GetBoneTransform(HumanBodyBones.Hips);
            if (!pelvis || !TryMeasureLeg(source, true, solePlaneY, pelvis, out left) ||
                !TryMeasureLeg(source, false, solePlaneY, pelvis, out right) ||
                left.upperLeg == right.upperLeg || left.foot == right.foot)
            {
                error = "The hips and both leg chains must be mapped as exposed transforms. Check the Avatar mapping and Optimize Game Objects import setting.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryMeasureLeg(Animator animator, bool left, float solePlaneY,
            Transform pelvis, out LegReferences leg)
        {
            leg = new LegReferences
            {
                upperLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                lowerLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot)
            };
            if (!leg.IsValid || !leg.upperLeg.IsChildOf(pelvis)) return false;

            float length = Vector3.Distance(leg.upperLeg.position, leg.lowerLeg.position) +
                Vector3.Distance(leg.lowerLeg.position, leg.foot.position);
            if (length <= 0.001f) return false;

            Vector3 ankle = leg.foot.position;
            Vector3 forward = animator.transform.forward;
            var toes = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            float toeDistance = length * 0.17f;
            if (toes)
            {
                Vector3 toeVector = Vector3.ProjectOnPlane(toes.position - ankle, Vector3.up);
                if (toeVector.magnitude > length * 0.045f &&
                    Vector3.Dot(toeVector.normalized, forward) > 0.5f)
                {
                    forward = toeVector.normalized;
                    toeDistance = Mathf.Clamp(toeVector.magnitude, length * 0.08f, length * 0.28f);
                }
            }

            float ankleHeight = ankle.y - solePlaneY;
            // Some models have a root pivot at the hips. Use a rig-relative estimate in that case.
            leg.soleHeight = ankleHeight >= length * 0.02f && ankleHeight <= length * 0.25f
                ? ankleHeight : length * 0.105f;
            Vector3 sole = ankle - Vector3.up * leg.soleHeight;
            leg.heelOffset = leg.foot.InverseTransformPoint(sole - forward * toeDistance * 0.45f);
            leg.toeOffset = leg.foot.InverseTransformPoint(sole + forward * toeDistance * 1.22f);
            leg.restRotation = Quaternion.Inverse(animator.transform.rotation) * leg.foot.rotation;
            leg.hasCalibratedRotation = true;
            return true;
        }
    }
}
