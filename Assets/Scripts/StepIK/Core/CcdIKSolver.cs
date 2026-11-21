using UnityEngine;

namespace StepIK
{
    /// <summary>Bounded distal-to-proximal cyclic coordinate descent for one biped leg.</summary>
    public static class CcdIKSolver
    {
        public static bool Calculate(Vector3 root, Vector3 knee, Vector3 ankle, Vector3 target,
            Vector3 hint, int iterations, float tolerance, out Vector3 solvedKnee,
            out Vector3 solvedAnkle, out int completedIterations)
        {
            solvedKnee = knee;
            solvedAnkle = ankle;
            completedIterations = 0;
            float upper = Vector3.Distance(root, knee);
            float lower = Vector3.Distance(knee, ankle);
            if (upper < 0.0001f || lower < 0.0001f) return false;

            Vector3 axis = target - root;
            if (axis.sqrMagnitude < 1e-8f) axis = ankle - root;
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.down;
            axis.Normalize();
            float reach = Mathf.Clamp(Vector3.Distance(root, target),
                Mathf.Abs(upper - lower) + 0.0001f, upper + lower - 0.0001f);
            target = root + axis * reach;
            Vector3 bend = TwoBoneIKSolver.StableBend(knee - root, ankle - root, target - root, hint - root);
            Vector3 planeNormal = Vector3.Cross(axis, bend).normalized;

            // A small bend escapes the straight-chain singularity without changing bone lengths.
            if (Vector3.ProjectOnPlane(solvedKnee - root, ankle - root).sqrMagnitude < 1e-6f)
            {
                solvedKnee = root + (knee - root + bend * (upper * 0.05f)).normalized * upper;
                solvedAnkle = solvedKnee + (ankle - solvedKnee).normalized * lower;
            }

            float toleranceSquared = Mathf.Max(0f, tolerance) * Mathf.Max(0f, tolerance);
            for (int i = 0; i < Mathf.Clamp(iterations, 1, 32); i++)
            {
                // Move the ankle toward the goal by rotating the knee joint first.
                solvedAnkle = solvedKnee + RotateToAlign(solvedAnkle - solvedKnee,
                    solvedAnkle - solvedKnee, target - solvedKnee, planeNormal);
                // Then rotate the entire lower chain about the hip.
                Vector3 currentTip = solvedAnkle - root;
                solvedKnee = root + RotateToAlign(solvedKnee - root, currentTip, target - root, planeNormal);
                solvedAnkle = root + RotateToAlign(currentTip, currentTip, target - root, planeNormal);
                completedIterations++;
                if ((solvedAnkle - target).sqrMagnitude <= toleranceSquared) break;
            }

            // Preserve the converged tip while placing the knee on its anatomical pole plane.
            Vector3 tipAxis = (solvedAnkle - root).normalized;
            Vector3 radial = Vector3.ProjectOnPlane(solvedKnee - root, tipAxis);
            Vector3 desired = Vector3.ProjectOnPlane(bend, tipAxis);
            if (radial.sqrMagnitude > 1e-8f && desired.sqrMagnitude > 1e-8f)
                solvedKnee = root + tipAxis * Vector3.Dot(solvedKnee - root, tipAxis) + desired.normalized * radial.magnitude;
            return true;
        }

        private static Vector3 RotateToAlign(Vector3 vector, Vector3 from, Vector3 to, Vector3 fallbackAxis)
        {
            if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return vector;
            Vector3 a = from.normalized, b = to.normalized;
            float cosine = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
            Vector3 cross = Vector3.Cross(a, b);
            float sine = cross.magnitude;
            if (sine < 1e-6f)
            {
                if (cosine >= 0f) return vector;
                Vector3 axis = Vector3.ProjectOnPlane(fallbackAxis, a).normalized;
                return -vector + 2f * axis * Vector3.Dot(axis, vector);
            }
            Vector3 unitAxis = cross / sine;
            return vector * cosine + Vector3.Cross(unitAxis, vector) * sine +
                unitAxis * Vector3.Dot(unitAxis, vector) * (1f - cosine);
        }

        public static void Apply(LegReferences leg, FootTarget target, Vector3 forward, int iterations)
        {
            if (target.weight <= 0.0001f) return;
            Vector3 root = leg.upperLeg.position, knee = leg.lowerLeg.position, ankle = leg.foot.position;
            Vector3 hint = leg.kneeHint ? leg.kneeHint.position : root + forward;
            Quaternion footRotation = Quaternion.Slerp(leg.foot.rotation, target.rotation, target.weight);
            if (!Calculate(root, knee, ankle, Vector3.Lerp(ankle, target.position, target.weight), hint,
                iterations, 0.0005f, out var newKnee, out var newAnkle, out _)) return;
            leg.upperLeg.rotation = Quaternion.FromToRotation(knee - root, newKnee - root) * leg.upperLeg.rotation;
            leg.lowerLeg.rotation = Quaternion.FromToRotation(leg.foot.position - leg.lowerLeg.position,
                newAnkle - leg.lowerLeg.position) * leg.lowerLeg.rotation;
            leg.foot.rotation = footRotation;
        }
    }
}
