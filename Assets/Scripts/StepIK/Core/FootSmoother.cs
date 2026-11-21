using UnityEngine;

namespace StepIK
{
    public sealed class FootSmoother
    {
        private bool initialized;
        private Vector3 position;
        private Quaternion rotation;
        private float weight;
        public static float Factor(float speed, float dt) => 1f - Mathf.Exp(-Mathf.Max(0f, speed) * Mathf.Max(0f, dt));

        public FootTarget Step(FootTarget target, Vector3 basePosition, Quaternion baseRotation,
            IKSettings settings, float dt)
        {
            if (!initialized)
            {
                position = basePosition; rotation = baseRotation; weight = 0f; initialized = true;
            }
            // Filter correction height without delaying the authored forward swing.
            position = new Vector3(target.position.x,
                Mathf.Lerp(position.y, target.position.y, Factor(settings.positionSpeed, dt)),
                target.position.z);
            rotation = Quaternion.Slerp(rotation, target.rotation, Factor(settings.rotationSpeed, dt));
            weight = Mathf.Lerp(weight, target.supported ? target.weight : 0f, Factor(settings.weightSpeed, dt));
            target.position = position; target.rotation = rotation; target.weight = weight;
            return target;
        }
        public void Reset() { initialized = false; weight = 0f; }
    }

    public sealed class PelvisController
    {
        public float Offset { get; private set; }
        public float Step(FootTarget left, FootTarget right, float leftBaseY, float rightBaseY,
            IKSettings settings, float dt)
        {
            float l = left.supported ? (left.position.y - leftBaseY) * left.weight : 0f;
            float r = right.supported ? (right.position.y - rightBaseY) * right.weight : 0f;
            float desired = Mathf.Clamp(Mathf.Min(0f, Mathf.Min(l, r)), -settings.maxPelvisDrop, 0f);
            Offset = Mathf.Lerp(Offset, desired, FootSmoother.Factor(settings.pelvisSpeed, dt));
            return Offset;
        }
        public void Reset() { Offset = 0f; }
    }
}
