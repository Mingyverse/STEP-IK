using System;
using UnityEngine;

namespace StepIK
{
    // Keep the original numeric values so existing scenes retain their settings.
    public enum TestMode { FKOnly, SingleRayIK, StepIK, UnityAnimatorIK, FabrikIK, CcdIK }

    [Serializable]
    public sealed class LegReferences
    {
        public Transform upperLeg;
        public Transform lowerLeg;
        public Transform foot;
        [Tooltip("Optional world-space knee hint, not parented to a corrected leg bone.")]
        public Transform kneeHint;
        [Tooltip("Heel/toe locations in FOOT LOCAL coordinates. Calibrate for the imported rig axes.")]
        public Vector3 heelOffset = new Vector3(0f, -0.08f, -0.06f);
        public Vector3 toeOffset = new Vector3(0f, -0.08f, 0.15f);
        [Tooltip("World-space vertical distance from sole support to the ankle pivot at rest.")]
        [Min(0f)] public float soleHeight = 0.08f;
        [Range(0f, 1f)] public float contactWeight = 1f;
        public bool hasCalibratedRotation;
        public Quaternion restRotation = Quaternion.identity;
        public bool IsValid => upperLeg && lowerLeg && foot &&
            upperLeg != lowerLeg && lowerLeg != foot &&
            lowerLeg.IsChildOf(upperLeg) && foot.IsChildOf(lowerLeg);
    }

    public struct FootContact
    {
        public bool valid;
        public Vector3 point;
        public Vector3 normal;
        public Collider collider;
    }

    public struct FootTarget
    {
        public bool supported;
        public Vector3 position;
        public Quaternion rotation;
        public float weight;
        public FootContact heelSupport, toeSupport;
    }

    public struct IKTelemetry
    {
        public int raycasts;
        public int cacheReuses;
        public bool leftSupported;
        public bool rightSupported;
        public float pelvisOffset;
        // This measures this script's elapsed execution time, NOT total frame time.
        public double scriptMilliseconds;
    }
}
