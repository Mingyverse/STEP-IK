using UnityEngine;

namespace StepIK
{
    /// <summary>Bounded FABRIK on a two-segment chain. No per-frame arrays or allocations.</summary>
    public static class FabrikIKSolver
    {
        public static bool Calculate(Vector3 root, Vector3 knee, Vector3 ankle, Vector3 target,
            Vector3 hint, int iterations, float tolerance, out Vector3 solvedKnee, out Vector3 solvedAnkle)
        {
            solvedKnee = knee; solvedAnkle = ankle;
            float a = Vector3.Distance(root, knee), b = Vector3.Distance(knee, ankle);
            if (a < 0.0001f || b < 0.0001f) return false;
            Vector3 axis = target - root;
            if (axis.sqrMagnitude < 0.00000001f) axis = Vector3.down;
            axis.Normalize();
            float d = Mathf.Clamp(Vector3.Distance(root, target), Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);
            target = root + axis * d;
            Vector3 bend = Vector3.ProjectOnPlane(hint - root, axis);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(knee - root, axis);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right);
            bend.Normalize();
            // Seed a straight leg away from the collinear singularity.
            if (Vector3.ProjectOnPlane(solvedKnee - root, axis).sqrMagnitude < 0.000001f)
                solvedKnee += bend * (a * 0.1f);
            for (int i = 0; i < Mathf.Clamp(iterations, 1, 32); i++)
            {
                solvedAnkle = target;
                solvedKnee = solvedAnkle + (solvedKnee - solvedAnkle).normalized * b;
                solvedKnee = root + (solvedKnee - root).normalized * a;
                solvedAnkle = solvedKnee + (solvedAnkle - solvedKnee).normalized * b;
                if ((solvedAnkle - target).sqrMagnitude <= tolerance * tolerance) break;
            }
            // Rotate the whole solution around its root-to-tip axis to honour the pole.
            Vector3 tipAxis = (solvedAnkle - root).normalized;
            Vector3 currentBend = Vector3.ProjectOnPlane(solvedKnee - root, tipAxis);
            Vector3 desiredBend = Vector3.ProjectOnPlane(bend, tipAxis);
            if (currentBend.sqrMagnitude > 0.000001f && desiredBend.sqrMagnitude > 0.000001f)
                solvedKnee = root + tipAxis * Vector3.Dot(solvedKnee - root, tipAxis) + desiredBend.normalized * currentBend.magnitude;
            return true;
        }

        public static void Apply(LegReferences leg, FootTarget target, Vector3 forward, int iterations)
        {
            if (target.weight <= 0.0001f) return;
            Vector3 root = leg.upperLeg.position, knee = leg.lowerLeg.position, ankle = leg.foot.position;
            Vector3 hint = leg.kneeHint ? leg.kneeHint.position : knee + forward * 0.1f;
            var rotation = Quaternion.Slerp(leg.foot.rotation, target.rotation, target.weight);
            if (!Calculate(root, knee, ankle, Vector3.Lerp(ankle, target.position, target.weight), hint,
                iterations, 0.0005f, out var newKnee, out var newAnkle)) return;
            leg.upperLeg.rotation = Quaternion.FromToRotation(knee - root, newKnee - root) * leg.upperLeg.rotation;
            leg.lowerLeg.rotation = Quaternion.FromToRotation(leg.foot.position - leg.lowerLeg.position,
                newAnkle - leg.lowerLeg.position) * leg.lowerLeg.rotation;
            leg.foot.rotation = rotation;
        }
    }
}
