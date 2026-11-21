using System;
using UnityEngine;

namespace StepIK
{
    [Serializable]
    public sealed class IKSettings
    {
        [Tooltip("Select ground only. Exclude character colliders.")]
        public LayerMask terrainMask;
        [Min(0.01f)] public float rayStartHeight = 0.5f;
        [Min(0.01f)] public float raycastDistance = 1.5f;
        [Range(0f, 89f)] public float maxSlopeAngle = 55f;
        [Min(0f)] public float maxContinuousHeightDifference = 0.12f;
        [Range(0f, 90f)] public float maxNormalDifference = 25f;
        [Min(0f)] public float maxFootCorrection = 0.5f;
        [Min(0f)] public float positionSpeed = 18f;
        [Min(0f)] public float rotationSpeed = 14f;
        [Min(0f)] public float weightSpeed = 12f;
        [Min(0f)] public float pelvisSpeed = 10f;
        [Min(0f)] public float maxPelvisDrop = 0.3f;
        [Min(0.001f)] public float cacheLifetime = 0.05f;
        [Min(0.001f)] public float resampleDistance = 0.025f;
        [Range(0.1f, 90f)] public float resampleAngle = 3f;
        [Min(0f)] public float fullUpdateSpeed = 1.5f;
        [Tooltip("Optional approximation, not a gait detector. Use explicit contact weights for final evaluation.")]
        public bool useHeightBasedSwingFade = true;
        [Min(0f)] public float swingFadeStart = 0.08f;
        [Min(0.001f)] public float swingFadeEnd = 0.22f;

        public void Clamp()
        {
            rayStartHeight = Mathf.Max(0.01f, rayStartHeight);
            raycastDistance = Mathf.Max(rayStartHeight + 0.01f, raycastDistance);
            maxSlopeAngle = Mathf.Clamp(maxSlopeAngle, 0f, 89f);
            maxContinuousHeightDifference = Mathf.Max(0f, maxContinuousHeightDifference);
            maxNormalDifference = Mathf.Clamp(maxNormalDifference, 0f, 90f);
            maxFootCorrection = Mathf.Max(0f, maxFootCorrection);
            positionSpeed = Mathf.Max(0f, positionSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            weightSpeed = Mathf.Max(0f, weightSpeed);
            pelvisSpeed = Mathf.Max(0f, pelvisSpeed);
            maxPelvisDrop = Mathf.Max(0f, maxPelvisDrop);
            cacheLifetime = Mathf.Max(0.001f, cacheLifetime);
            resampleDistance = Mathf.Max(0.001f, resampleDistance);
            resampleAngle = Mathf.Clamp(resampleAngle, 0.1f, 90f);
            fullUpdateSpeed = Mathf.Max(0f, fullUpdateSpeed);
            swingFadeStart = Mathf.Max(0f, swingFadeStart);
            swingFadeEnd = Mathf.Max(swingFadeStart + 0.001f, swingFadeEnd);
        }
    }
}
