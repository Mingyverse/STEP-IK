using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StepIK.Sandbox
{
    public static class BenchmarkScoring
    {
        public const string Formula = "Each metric contributes weight / (1 + value / reference). " +
            "Weights are 35, 25, 10, 12, 8, 7, 3. References are " +
            "30 mm stance gap, 10% clipped foot-frames, 60 degrees/s, 16.67 ms frame, " +
            "2 ms managed IK, 30% process CPU, and 2048 MiB RAM. Terrains are equally weighted.";
        private static readonly double[] Weights = { 35, 25, 10, 12, 8, 7, 3 };
        private static readonly double[] References = { 30, 10, 60, 16.67, 2, 30, 2048 };

        public static double Score(params double[] values)
        {
            if (values.Length != Weights.Length) throw new ArgumentException("Expected seven benchmark metrics.");
            double score = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || values[i] < 0) return double.NaN;
                score += Weights[i] / (1 + values[i] / References[i]);
            }
            return score;
        }

        public static IEnumerable<TrialSummary> Aggregate(IEnumerable<TrialSummary> trials)
        {
            return trials.GroupBy(x => (x.agentCount, x.method)).Select(g => new TrialSummary(g.Key.method)
            {
                agentCount = g.Key.agentCount,
                score = g.Average(t => t.score),
                meanStanceGapMm = g.Average(t => t.meanStanceGapMm),
                clipPercent = g.Average(t => t.clipPercent),
                meanCorrectionDegreesPerSecond = g.Average(t => t.meanCorrectionDegreesPerSecond),
                meanFrameMs = g.Average(t => t.meanFrameMs),
                meanScriptMs = g.Average(t => t.meanScriptMs),
                processCpuPercent = g.Average(t => t.processCpuPercent),
                peakWorkingSetMiB = g.Max(t => t.peakWorkingSetMiB),
                clippingEvents = g.Sum(t => t.clippingEvents),
                frameCount = g.Sum(t => t.frameCount),
                validProbes = g.Sum(t => t.validProbes),
                stanceProbes = g.Sum(t => t.stanceProbes),
                observedFeet = g.Sum(t => t.observedFeet)
            }).OrderBy(t => t.agentCount).ThenByDescending(t => double.IsNaN(t.score) ? -1 : t.score);
        }

        public static string Csv(IEnumerable<TrialSummary> rows)
        {
            var csv = new StringBuilder("world,method,score,stance_gap_mm,clipped_foot_percent,clipping_events,rotation_deg_per_s,frame_ms,managed_ik_ms,process_cpu_percent,peak_working_set_mib,walkers,frame_count,valid_probes,stance_probes,observed_feet,probe_hit_percent\n");
            foreach (var r in rows)
                csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}\n",
                    BenchmarkExport.CsvText(r.world), BenchmarkExport.CsvText(r.method),
                    BenchmarkExport.CsvNumber(r.score), BenchmarkExport.CsvNumber(r.meanStanceGapMm),
                    BenchmarkExport.CsvNumber(r.clipPercent), r.clippingEvents,
                    BenchmarkExport.CsvNumber(r.meanCorrectionDegreesPerSecond), BenchmarkExport.CsvNumber(r.meanFrameMs),
                    BenchmarkExport.CsvNumber(r.meanScriptMs, "F4"), BenchmarkExport.CsvNumber(r.processCpuPercent),
                    BenchmarkExport.CsvNumber(r.peakWorkingSetMiB, "F1"), r.agentCount,
                    r.frameCount, r.validProbes, r.stanceProbes, r.observedFeet, BenchmarkExport.CsvNumber(r.ProbeHitPercent));
            return csv.ToString();
        }
    }
}
