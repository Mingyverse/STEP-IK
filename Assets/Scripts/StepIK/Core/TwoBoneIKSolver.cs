using UnityEngine;

namespace StepIK
{
    public static class TwoBoneIKSolver
    {
        // Law of cosines: desired knee = root + forward * adjacent + bend * height.
        public static bool Calculate(Vector3 root, Vector3 knee, Vector3 ankle, Vector3 target,
            Vector3 hint, out Vector3 solvedKnee, out Vector3 solvedAnkle)
        {
            return Calculate(root, knee, ankle, target, hint, Vector3.Distance(root, knee),
                Vector3.Distance(knee, ankle), 0f, out solvedKnee, out solvedAnkle);
        }

        private static bool Calculate(Vector3 root, Vector3 knee, Vector3 ankle, Vector3 target,
            Vector3 hint, float upper, float lower, float minimumReach, out Vector3 solvedKnee, out Vector3 solvedAnkle)
        {
            solvedKnee = knee; solvedAnkle = ankle;
            if (upper < 0.0001f || lower < 0.0001f) return false;
            Vector3 direction = target - root;
            if (direction.sqrMagnitude < 0.00000001f) direction = ankle - root;
            if (direction.sqrMagnitude < 0.00000001f) direction = Vector3.down;
            direction.Normalize();
            float distance = Mathf.Clamp(Vector3.Distance(root, target),
                Mathf.Max(minimumReach, Mathf.Abs(upper - lower) + 0.00001f), upper + lower - 0.00001f);
            Vector3 bend = Vector3.ProjectOnPlane(hint - root, direction);
            if (bend.sqrMagnitude < 0.00000001f)
                bend = Vector3.ProjectOnPlane(knee - root, direction);
            if (bend.sqrMagnitude < 0.00000001f)
                bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right);
            bend.Normalize();
            float adjacent = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - adjacent * adjacent));
            solvedKnee = root + direction * adjacent + bend * height;
            solvedAnkle = root + direction * distance;
            return true;
        }

        public static void Apply(LegReferences leg, FootTarget target, Vector3 defaultBendDirection)
        {
            if (target.weight <= 0.0001f) return;
            Vector3 root = leg.upperLeg.position;
            Vector3 knee = leg.lowerLeg.position;
            Vector3 ankle = leg.foot.position;
            Vector3 blendedTarget = Vector3.Lerp(ankle, target.position, target.weight);
            Quaternion footRotation = Quaternion.Slerp(leg.foot.rotation, target.rotation, target.weight);
            if ((blendedTarget - ankle).sqrMagnitude < 0.00000025f)
            {
                leg.foot.rotation = footRotation;
                return;
            }
            Vector3 forward = leg.kneeHint ? leg.kneeHint.position - root : defaultBendDirection;
            Vector3 bend = StableBend(knee - root, ankle - root, blendedTarget - root, forward);
            float upper = Vector3.Distance(root, knee);
            float lower = Vector3.Distance(knee, ankle);
            // Bound terrain-induced folding to 125 degrees of flexion. Keep deeper authored poses reachable.
            float minimumReach = Mathf.Sqrt(Mathf.Max(0f, upper * upper + lower * lower - 1.147153f * upper * lower));
            minimumReach = Mathf.Min(minimumReach, Vector3.Distance(root, ankle));
            if (!Calculate(root, knee, ankle, blendedTarget, root + bend, upper, lower, minimumReach,
                out var newKnee, out var newAnkle)) return;
            leg.upperLeg.rotation = Quaternion.FromToRotation(knee - root, newKnee - root) * leg.upperLeg.rotation;
            leg.lowerLeg.rotation = Quaternion.FromToRotation(leg.foot.position - leg.lowerLeg.position,
                newAnkle - leg.lowerLeg.position) * leg.lowerLeg.rotation;
            leg.foot.rotation = footRotation;
        }

        public static Vector3 StableBend(Vector3 posedKnee, Vector3 posedAnkle, Vector3 goalAnkle, Vector3 forward)
        {
            Vector3 axis = goalAnkle.sqrMagnitude > 1e-8f ? goalAnkle.normalized : Vector3.down;
            Vector3 anatomical = Vector3.ProjectOnPlane(forward, axis);
            if (anatomical.sqrMagnitude < 1e-6f)
                anatomical = Vector3.ProjectOnPlane(Mathf.Abs(axis.z) < 0.85f ? Vector3.forward : Vector3.right, axis);
            Vector3 reference = anatomical.normalized;
            Vector3 authored = Vector3.ProjectOnPlane(
                Vector3.ProjectOnPlane(posedKnee, posedAnkle), axis);
            if (authored.sqrMagnitude < 1e-6f) return reference;

            float reliability = Mathf.Clamp01(authored.magnitude / Mathf.Max(0.005f, posedAnkle.magnitude * 0.12f));
            // Keep the useful component of the FK pole and cap sideways deflection to about 40 degrees.
            Vector3 sideways = authored.normalized - Vector3.Dot(authored.normalized, reference) * reference;
            return (reference + sideways * (0.84f * reliability)).normalized;
        }
    }
}
