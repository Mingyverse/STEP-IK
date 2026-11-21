using System;
using UnityEngine;

namespace StepIK.Sandbox
{
    [Serializable]
    public struct BenchmarkFrame
    {
        public float time, frameMs, meanAbsoluteGap, stanceMeanAbsoluteGap, correctionDegreesPerSecond;
        public double scriptMs;
        public int validProbes, stanceProbes, observedFeet, clippedFootFrames, clippingEvents, groundQueries, cachedFeet;
    }

    public sealed class BenchmarkMetrics
    {
        private readonly bool[] wasClipping;
        private readonly Quaternion[] previousCorrection;
        private readonly bool[] hasPrevious;
        public BenchmarkMetrics(int maxActors)
        {
            wasClipping = new bool[maxActors * 2];
            previousCorrection = new Quaternion[maxActors * 2];
            hasPrevious = new bool[maxActors * 2];
        }
        public const float ClipThreshold = 0.01f;

        public void Reset()
        {
            Array.Clear(wasClipping, 0, wasClipping.Length);
            Array.Clear(hasPrevious, 0, hasPrevious.Length);
        }

        public BenchmarkFrame Measure(StepIKController[] actors, int count, float time, float dt)
        {
            var frame = new BenchmarkFrame { time = time, frameMs = dt * 1000f };
            float sum = 0f, stanceSum = 0f, angularSum = 0f;
            int angularCount = 0;
            for (int i = 0; i < count; i++)
            {
                var actor = actors[i];
                frame.scriptMs += actor.Telemetry.scriptMilliseconds;
                frame.groundQueries += actor.Telemetry.raycasts;
                frame.cachedFeet += actor.Telemetry.cacheReuses;
                MeasureFoot(actor.leftLeg, actor.LeftBaseRotation, actor.settings.terrainMask, i * 2,
                    dt, ref frame, ref sum, ref stanceSum, ref angularSum, ref angularCount);
                MeasureFoot(actor.rightLeg, actor.RightBaseRotation, actor.settings.terrainMask, i * 2 + 1,
                    dt, ref frame, ref sum, ref stanceSum, ref angularSum, ref angularCount);
            }
            frame.meanAbsoluteGap = frame.validProbes > 0 ? sum / frame.validProbes : float.NaN;
            frame.stanceMeanAbsoluteGap = frame.stanceProbes > 0 ? stanceSum / frame.stanceProbes : float.NaN;
            frame.correctionDegreesPerSecond = angularCount > 0 ? angularSum / angularCount : 0f;
            return frame;
        }

        private void MeasureFoot(LegReferences leg, Quaternion baseRotation, LayerMask ground, int index,
            float dt, ref BenchmarkFrame frame, ref float sum, ref float stanceSum, ref float angularSum, ref int angularCount)
        {
            bool clipping = false;
            int before = frame.validProbes;
            Probe(leg.foot.TransformPoint(leg.heelOffset), ground, leg.contactWeight, ref clipping, ref frame, ref sum, ref stanceSum);
            Probe(leg.foot.TransformPoint(leg.toeOffset), ground, leg.contactWeight, ref clipping, ref frame, ref sum, ref stanceSum);
            if (frame.validProbes == before)
            {
                hasPrevious[index] = false;
                return;
            }
            frame.observedFeet++;
            if (clipping) frame.clippedFootFrames++;
            if (clipping && !wasClipping[index]) frame.clippingEvents++;
            wasClipping[index] = clipping;
            Quaternion correction = leg.foot.rotation * Quaternion.Inverse(baseRotation);
            if (hasPrevious[index] && dt > 0f)
            {
                angularSum += Quaternion.Angle(previousCorrection[index], correction) / dt;
                angularCount++;
            }
            previousCorrection[index] = correction; hasPrevious[index] = true;
        }

        private static void Probe(Vector3 sole, LayerMask ground, float contactWeight,
            ref bool clipping, ref BenchmarkFrame frame, ref float sum, ref float stanceSum)
        {
            // Independent measurement queries: identical for every method, not solver ray counts.
            if (!Physics.Raycast(sole + Vector3.up * 3f, Vector3.down, out var hit, 8f,
                ground, QueryTriggerInteraction.Ignore)) return;
            float gap = sole.y - hit.point.y;
            frame.validProbes++; sum += Mathf.Abs(gap);
            if (contactWeight >= 0.5f) { frame.stanceProbes++; stanceSum += Mathf.Abs(gap); }
            clipping |= gap < -ClipThreshold;
        }
    }
}
