using System;
using UnityEngine;
using UnityEditor;

namespace StepIK.Editor
{
    public static class StepIKMathChecks
    {
        [MenuItem("Tools/STEP-IK/Run Math Checks")]
        public static void RunInEditor()
        {
            int count = RunAll();
            Debug.Log($"STEP-IK: {count} math checks passed. Inspect the live rig and benchmark results separately.");
        }

        public static int RunAll()
        {
            int count = 0;
            Vector3 root = Vector3.zero, knee = new Vector3(0f, -1f, 0f), ankle = new Vector3(0f, -2f, 0f);
            Vector3 hint = new Vector3(0f, -1f, 1f);
            foreach (var target in new[] { new Vector3(0f, -1.5f, 0.2f), new Vector3(0f, -8f, 0f), Vector3.zero })
            {
                Require(TwoBoneIKSolver.Calculate(root, knee, ankle, target, hint, out var k, out var a), "Valid chain rejected");
                Near(Vector3.Distance(root, k), 1f, 0.001f, "Upper-leg length");
                Near(Vector3.Distance(k, a), 1f, 0.001f, "Lower-leg length");
                Require(!float.IsNaN(k.x) && !float.IsNaN(a.y), "Non-finite output");
                if (target.magnitude > 0.01f && target.magnitude < 2f)
                    Near(Vector3.Distance(a, target), 0f, 0.001f, "Reachable target");
                count++;
            }
            Require(!TwoBoneIKSolver.Calculate(root, root, ankle, ankle, hint, out _, out _), "Zero-length bone accepted"); count++;
            var settings = new IKSettings();
            FootContact low = new FootContact { valid = true, point = Vector3.zero, normal = Vector3.up };
            FootContact high = new FootContact { valid = true, point = new Vector3(0f, 0.08f, 0.1f), normal = Vector3.up };
            Near(TerrainDetector.SelectSupport(low, high, settings).point.y, 0.04f, 0.0001f, "Mean support"); count++;
            high.point.y = 0.3f;
            Near(TerrainDetector.SelectSupport(low, high, settings).point.y, 0.3f, 0.0001f, "Step fallback"); count++;
            Near(TerrainDetector.SelectSupport(default, high, settings).point.y, 0.3f, 0.0001f, "Partial support"); count++;
            Require(!TerrainDetector.SelectSupport(default, default, settings).valid, "No-contact fallback"); count++;
            float oneStep = FootSmoother.Factor(10f, 0.04f);
            float halfStep = FootSmoother.Factor(10f, 0.02f);
            Near(oneStep, 1f - (1f - halfStep) * (1f - halfStep), 0.00001f, "Time-based filter"); count++;
            Near(FootSmoother.Factor(10f, 0f), 0f, 0.0001f, "Paused filter"); count++;
            foreach (var target in new[] { new Vector3(0f, -1.5f, 0.2f), new Vector3(0f, -8f, 0f), Vector3.zero })
            {
                Require(FabrikIKSolver.Calculate(root, knee, ankle, target, hint, 8, 0.0005f, out var k, out var a), "FABRIK valid chain rejected");
                Near(Vector3.Distance(root, k), 1f, 0.001f, "FABRIK upper length");
                Near(Vector3.Distance(k, a), 1f, 0.001f, "FABRIK lower length");
                Require(!float.IsNaN(a.y), "FABRIK finite output");
                if (target.magnitude > 0.1f && target.magnitude < 1.9f)
                    Near(Vector3.Distance(a, target), 0f, 0.015f, "FABRIK reachable target convergence");
                count++;
            }
            Require(!FabrikIKSolver.Calculate(root, root, ankle, ankle, hint, 8, 0.001f, out _, out _), "FABRIK degenerate chain accepted"); count++;
            Near(Sandbox.SandboxTerrain.HeightAt(0, 0f, 5f), 0f, 0.0001f, "Flat height"); count++;
            Near(Sandbox.SandboxTerrain.HeightAt(1, 0f, 7f), 0.8f, 0.0001f, "Ramp plateau"); count++;
            Near(Sandbox.SandboxTerrain.HeightAt(1, 0f, 15f), 0f, 0.0001f, "Descending slope"); count++;
            Near(Sandbox.SandboxTerrain.HeightAt(2, 0f, 3.25f), 0.12f, 0.0001f, "First stair tread"); count++;
            Near(Sandbox.SandboxTerrain.HeightAt(2, 0f, 13f), 0f, 0.0001f, "Stair exit"); count++;
            var plane = new FootContact { valid = true, point = Vector3.zero, normal = new Vector3(0f, 1f, -0.2f) };
            Near(TerrainDetector.AnkleHeight(plane, Vector3.zero, new Vector3(0f, -0.1f, 0.25f)), 0.15f, 0.0001f, "Sole height on inclined plane"); count++;
            Near((float)Sandbox.BenchmarkScoring.Score(0, 0, 0, 0, 0, 0, 0), 100f, 0.001f, "Score maximum"); count++;
            Near((float)Sandbox.BenchmarkScoring.Score(30, 10, 60, 16.67, 2, 30, 2048), 50f, 0.001f, "Score at references"); count++;
            Require(double.IsNaN(Sandbox.BenchmarkScoring.Score(double.NaN, 0, 0, 0, 0, 0, 0)), "Missing metrics must not score"); count++;
            var frames = new System.Collections.Generic.List<Sandbox.BenchmarkFrame>
            {
                new Sandbox.BenchmarkFrame { meanAbsoluteGap = 0.02f, validProbes = 4,
                    stanceMeanAbsoluteGap = 0.03f, stanceProbes = 2, observedFeet = 2, clippedFootFrames = 1 },
                new Sandbox.BenchmarkFrame { meanAbsoluteGap = float.NaN, stanceMeanAbsoluteGap = float.NaN }
            };
            var trial = new Sandbox.TrialSummary("Test", "FK", frames, 10, 104857600);
            Near((float)trial.meanStanceGapMm, 30f, 0.001f, "Missing stance samples cannot poison mean"); count++;
            Near((float)trial.clipPercent, 50f, 0.001f, "Clipping rate uses observed feet"); count++;
            trial.agentCount = 1;
            Near((float)trial.ProbeHitPercent, 50f, 0.001f, "Coverage includes missing probe opportunities"); count++;
            var emptyTrial = new Sandbox.TrialSummary("Test", "FK", new System.Collections.Generic.List<Sandbox.BenchmarkFrame>(), double.NaN, 0);
            Require(double.IsNaN(emptyTrial.meanStanceGapMm) && double.IsNaN(emptyTrial.clipPercent) &&
                double.IsNaN(emptyTrial.score) && double.IsNaN(emptyTrial.ProbeHitPercent), "Unmeasured trial metrics are unavailable"); count++;
            Require(Sandbox.BenchmarkExport.CsvNumber(double.NaN) == "" &&
                Sandbox.BenchmarkExport.CsvText("Hill, \"East\"") == "\"Hill, \"\"East\"\"\"", "CSV escapes text and leaves missing values blank"); count++;
            string rawJson = "{\n  \"method\": \"NaN\",\n  \"score\": NaN,\n  \"other\": -Infinity\n}";
            Require(Sandbox.BenchmarkExport.NormalizeMissingJsonNumbers(rawJson) ==
                "{\n  \"method\": \"NaN\",\n  \"score\": null,\n  \"other\": null\n}", "JSON missing values preserve quoted labels"); count++;
            var stable = TwoBoneIKSolver.StableBend(new Vector3(0.4f, -1f, -0.7f), Vector3.down * 2f,
                new Vector3(0f, -1.5f, 0.2f), Vector3.forward);
            Require(Vector3.Dot(stable, Vector3.forward) > 0.70f, "Analytical knee pole must remain forward"); count++;
            var straight = TwoBoneIKSolver.StableBend(Vector3.down, Vector3.down * 2f, Vector3.down, Vector3.forward);
            Require(Vector3.Dot(straight, Vector3.forward) > 0.999f, "Straight leg falls back to forward pole"); count++;
            var sprint = new Sandbox.ManualSprintState();
            sprint.Step(true, true, true);
            Require(sprint.Active, "Sprint starts on a Shift request while moving"); count++;
            sprint.Step(true, false, false);
            Require(sprint.Active, "Sprint remains latched after Shift is released"); count++;
            sprint.Step(false, false, false);
            Require(!sprint.Active, "Stopping resets sprint"); count++;
            sprint.Step(true, false, false);
            Require(!sprint.Active, "Walking after a stop requires a new sprint request"); count++;
            sprint.Step(false, false, true);
            sprint.Step(true, false, true);
            Require(sprint.Active, "Holding Shift before moving starts a sprint"); count++;
            foreach (var target in new[] { new Vector3(0f, -1.5f, 0.2f), new Vector3(0f, -8f, 0f), Vector3.zero })
            {
                Require(CcdIKSolver.Calculate(root, knee, ankle, target, hint, 24, 0.0005f,
                    out var ccdKnee, out var ccdAnkle, out var sweeps), "CCD valid chain rejected");
                Near(Vector3.Distance(root, ccdKnee), 1f, 0.001f, "CCD upper length");
                Near(Vector3.Distance(ccdKnee, ccdAnkle), 1f, 0.001f, "CCD lower length");
                Require(!float.IsNaN(ccdKnee.x) && !float.IsNaN(ccdAnkle.y) && sweeps <= 24, "CCD bounded finite solve");
                if (target.magnitude > 0.1f && target.magnitude < 1.9f)
                {
                    Near(Vector3.Distance(ccdAnkle, target), 0f, 0.002f, "CCD reachable target convergence");
                    Require(Vector3.Dot(Vector3.ProjectOnPlane(ccdKnee, ccdAnkle).normalized, Vector3.forward) > 0.7f,
                        "CCD knee must honour the forward pole");
                }
                count++;
            }
            Require(!CcdIKSolver.Calculate(root, root, ankle, ankle, hint, 12, 0.0005f, out _, out _, out _),
                "CCD zero-length chain accepted"); count++;
            CcdIKSolver.Calculate(root, knee, ankle, new Vector3(0f, -1.5f, 0.2f), hint, 1, 0.0005f,
                out _, out var oneSweep, out var performed);
            Require(performed == 1, "CCD iteration limit ignored"); count++;
            Vector3 reachable = new Vector3(0f, -1.5f, 0.2f);
            CcdIKSolver.Calculate(root, knee, ankle, reachable, hint, 12, 0.0005f,
                out _, out var defaultSweep, out _);
            Require(Vector3.Distance(defaultSweep, reachable) < Vector3.Distance(oneSweep, reachable),
                "Additional CCD sweeps must improve this reachable target"); count++;
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("STEP-IK check failed: " + message);
        }
        private static void Near(float actual, float expected, float tolerance, string message)
        {
            Require(!float.IsNaN(actual) && Mathf.Abs(actual - expected) <= tolerance,
                message + $" (expected {expected}, actual {actual})");
        }
    }
}
