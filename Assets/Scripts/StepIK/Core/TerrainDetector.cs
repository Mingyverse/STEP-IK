using UnityEngine;

namespace StepIK
{
    public sealed class TerrainDetector
    {
        private readonly RaycastHit[] hits = new RaycastHit[16];

        public FootContact Sample(Vector3 sole, Transform characterRoot, IKSettings settings)
        {
            Vector3 origin = sole + Vector3.up * settings.rayStartHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits,
                settings.raycastDistance, settings.terrainMask, QueryTriggerInteraction.Ignore);
            // A full buffer may omit the nearest usable hit. Fail safely rather than guess.
            if (count == hits.Length) return default;
            float nearest = float.PositiveInfinity;
            RaycastHit chosen = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(characterRoot)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                chosen = hit;
                found = true;
            }
            // Reject the nearest surface if too steep; do not ray through it to a floor below.
            if (!found || Vector3.Angle(chosen.normal, Vector3.up) > settings.maxSlopeAngle)
                return default;
            return new FootContact { valid = true, point = chosen.point,
                normal = chosen.normal.normalized, collider = chosen.collider };
        }

        public static FootContact SelectSupport(FootContact heel, FootContact toe, IKSettings settings)
        {
            if (!heel.valid) return toe;
            if (!toe.valid) return heel;
            if (Mathf.Abs(heel.point.y - toe.point.y) > settings.maxContinuousHeightDifference ||
                Vector3.Angle(heel.normal, toe.normal) > settings.maxNormalDifference)
                return heel.point.y >= toe.point.y ? heel : toe;
            Vector3 normal = heel.normal + toe.normal;
            if (normal.sqrMagnitude < 0.000001f) return default;
            return new FootContact { valid = true, point = (heel.point + toe.point) * 0.5f,
                normal = normal.normalized, collider = heel.collider };
        }

        public static FootTarget BuildTarget(FootContact contact, Vector3 basePosition,
            Quaternion baseRotation, Vector3 characterUp, LegReferences leg, IKSettings settings)
        {
            var result = new FootTarget { position = basePosition, rotation = baseRotation };
            if (!contact.valid) return result;
            float height = contact.point.y + leg.soleHeight;
            if (Mathf.Abs(height - basePosition.y) > settings.maxFootCorrection) return result;
            result.supported = true;
            result.position.y = height;
            // Relative tilt preserves the rig's imported ankle axes and authored heading.
            result.rotation = Quaternion.FromToRotation(characterUp, contact.normal) * baseRotation;
            result.weight = leg.contactWeight;
            if (settings.useHeightBasedSwingFade)
                result.weight *= 1f - Mathf.InverseLerp(settings.swingFadeStart,
                    settings.swingFadeEnd, basePosition.y - height);
            return result;
        }

        public static FootTarget BuildCalibratedTarget(FootContact support, FootContact heel, FootContact toe,
            Transform root, LegReferences leg, IKSettings settings, bool single)
        {
            var foot = leg.foot;
            var result = new FootTarget { position = foot.position, rotation = foot.rotation };
            if (!support.valid) return result;

            Quaternion rotation = Quaternion.FromToRotation(root.up, support.normal) * root.rotation * leg.restRotation;
            Quaternion delta = rotation * Quaternion.Inverse(foot.rotation);
            Vector3 heelOffset = delta * foot.TransformVector(leg.heelOffset);
            Vector3 toeOffset = delta * foot.TransformVector(leg.toeOffset);
            float height;
            if (single)
                height = AnkleHeight(support, foot.position, (heelOffset + toeOffset) * 0.5f);
            else if (heel.valid && toe.valid)
                height = Mathf.Max(AnkleHeight(heel, foot.position, heelOffset),
                    AnkleHeight(toe, foot.position, toeOffset));
            else
                height = AnkleHeight(support, foot.position, heel.valid ? heelOffset : toeOffset);

            if (Mathf.Abs(height - foot.position.y) > settings.maxFootCorrection) return result;
            result.supported = true;
            result.position.y = height;
            result.rotation = rotation;
            result.weight = leg.contactWeight;
            if (settings.useHeightBasedSwingFade)
                result.weight *= 1f - Mathf.InverseLerp(settings.swingFadeStart,
                    settings.swingFadeEnd, foot.position.y - height);
            if (!single)
            {
                result.heelSupport = heel;
                result.toeSupport = toe;
            }
            return result;
        }

        public static FootTarget PreserveClearance(FootTarget target, LegReferences leg)
        {
            if (!target.heelSupport.valid && !target.toeSupport.valid) return target;
            var foot = leg.foot;
            Vector3 position = Vector3.Lerp(foot.position, target.position, target.weight);
            Quaternion rotation = Quaternion.Slerp(foot.rotation, target.rotation, target.weight);
            Quaternion delta = rotation * Quaternion.Inverse(foot.rotation);
            float floor = float.NegativeInfinity;
            if (target.heelSupport.valid)
                floor = AnkleHeight(target.heelSupport, position, delta * foot.TransformVector(leg.heelOffset));
            if (target.toeSupport.valid)
                floor = Mathf.Max(floor, AnkleHeight(target.toeSupport, position, delta * foot.TransformVector(leg.toeOffset)));

            // Cap the lift to preserve the authored swing when only minor clearance is needed.
            float lift = Mathf.Clamp(floor - position.y - 0.002f, 0f, 0.12f);
            if (lift <= 0f) return target;
            target.position = position + Vector3.up * lift;
            target.rotation = rotation;
            target.weight = 1f;
            return target;
        }

        public static float AnkleHeight(FootContact support, Vector3 ankle, Vector3 soleOffset)
        {
            Vector3 sample = ankle + soleOffset;
            Vector3 offset = sample - support.point;
            float height = support.point.y - (support.normal.x * offset.x + support.normal.z * offset.z) /
                Mathf.Max(0.1f, support.normal.y);
            return height - soleOffset.y;
        }
    }
}
