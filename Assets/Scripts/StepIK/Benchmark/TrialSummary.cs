using System;
using System.Collections.Generic;

namespace StepIK.Sandbox
{
    [Serializable]
    public sealed class TrialSummary
    {
        public string world, method;
        public int frameCount, validProbes, stanceProbes, observedFeet, clippingEvents, clippedFootFrames;
        public int agentCount;
        public double clipPercent = double.NaN, score = double.NaN;
        public double meanGapMm, meanStanceGapMm, meanFrameMs, meanScriptMs;
        public double meanCorrectionDegreesPerSecond, processCpuPercent, peakWorkingSetMiB;
        public double meanQueries, meanCachedFeet;
        public double ProbeHitPercent => frameCount > 0 && agentCount > 0
            ? 100d * validProbes / (4d * frameCount * agentCount)
            : double.NaN;

        internal TrialSummary(string method)
        {
            world = "Combined";
            this.method = method;
        }

        public TrialSummary(string world, string method, List<BenchmarkFrame> frames, double cpu, long workingSet)
        {
            this.world = world;
            this.method = method;
            frameCount = frames.Count;
            processCpuPercent = cpu;
            peakWorkingSetMiB = workingSet > 0 ? workingSet / 1048576d : double.NaN;
            double weightedGap = 0d, weightedStanceGap = 0d;
            foreach (var frame in frames)
            {
                validProbes += frame.validProbes;
                stanceProbes += frame.stanceProbes;
                if (frame.validProbes > 0) weightedGap += frame.meanAbsoluteGap * frame.validProbes;
                if (frame.stanceProbes > 0) weightedStanceGap += frame.stanceMeanAbsoluteGap * frame.stanceProbes;
                observedFeet += frame.observedFeet;
                clippingEvents += frame.clippingEvents;
                clippedFootFrames += frame.clippedFootFrames;
                meanFrameMs += frame.frameMs;
                meanScriptMs += frame.scriptMs;
                meanCorrectionDegreesPerSecond += frame.correctionDegreesPerSecond;
                meanQueries += frame.groundQueries;
                meanCachedFeet += frame.cachedFeet;
            }
            meanGapMm = validProbes > 0 ? 1000d * weightedGap / validProbes : double.NaN;
            meanStanceGapMm = stanceProbes > 0 ? 1000d * weightedStanceGap / stanceProbes : double.NaN;
            if (frameCount == 0)
            {
                meanFrameMs = meanScriptMs = meanCorrectionDegreesPerSecond = meanQueries = meanCachedFeet = double.NaN;
                return;
            }
            meanFrameMs /= frameCount;
            meanScriptMs /= frameCount;
            meanCorrectionDegreesPerSecond /= frameCount;
            meanQueries /= frameCount;
            meanCachedFeet /= frameCount;
            clipPercent = observedFeet > 0 ? 100d * clippedFootFrames / observedFeet : double.NaN;
            if (stanceProbes > 0 && observedFeet > 0 && peakWorkingSetMiB > 0)
                score = BenchmarkScoring.Score(meanStanceGapMm, clipPercent, meanCorrectionDegreesPerSecond,
                    meanFrameMs, meanScriptMs, processCpuPercent, peakWorkingSetMiB);
        }

    }
}
