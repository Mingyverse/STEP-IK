using UnityEngine;

namespace StepIK
{
    // Cache measured contacts, then reconstruct targets from the CURRENT FK pose.
    // This avoids freezing a swing foot's horizontal motion on skipped sampling frames.
    public sealed class FootTargetCache
    {
        public FootContact Heel { get; private set; }
        public FootContact Toe { get; private set; }
        private bool valid;
        private Vector3 heelPosition, toePosition;
        private Quaternion rotation;
        private float sampledAt;

        public bool CanReuse(Vector3 heel, Vector3 toe, Quaternion currentRotation,
            float now, IKSettings settings)
        {
            float threshold = settings.resampleDistance * settings.resampleDistance;
            return valid && now >= sampledAt && now - sampledAt < settings.cacheLifetime &&
                (heel - heelPosition).sqrMagnitude < threshold &&
                (toe - toePosition).sqrMagnitude < threshold &&
                Quaternion.Angle(rotation, currentRotation) < settings.resampleAngle &&
                Alive(Heel) && Alive(Toe);
        }

        private static bool Alive(FootContact contact) => !contact.valid ||
            (contact.collider && contact.collider.enabled && contact.collider.gameObject.activeInHierarchy);

        public void Store(FootContact heel, FootContact toe, Vector3 heelOrigin,
            Vector3 toeOrigin, Quaternion currentRotation, float now)
        {
            Heel = heel; Toe = toe;
            heelPosition = heelOrigin; toePosition = toeOrigin;
            rotation = currentRotation; sampledAt = now;
            valid = heel.valid || toe.valid;
        }

        public void Invalidate() { valid = false; }
    }
}
