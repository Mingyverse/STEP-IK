using UnityEngine;
using Unity.Profiling;
using StepIK;

/// <summary>Prototype correction layer. Requires an upright, uniformly scaled rig and Normal Animator update mode.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class StepIKController : MonoBehaviour
{
    public Animator animator;
    public Transform pelvis;
    public LegReferences leftLeg = new LegReferences();
    public LegReferences rightLeg = new LegReferences();
    public IKSettings settings = new IKSettings();
    public TestMode testMode = TestMode.StepIK;
    public bool adaptiveUpdates = true;
    public bool isRunning = true;
    [Range(1, 32)] public int fabrikIterations = 8;
    [Range(1, 32)] public int ccdIterations = 12;
    public IKTelemetry Telemetry { get; private set; }
    public Quaternion LeftBaseRotation { get; private set; }
    public Quaternion RightBaseRotation { get; private set; }
    public bool BuiltInCallbackSeen { get; private set; }

    private readonly TerrainDetector detector = new TerrainDetector();
    private readonly FootTargetCache leftCache = new FootTargetCache(), rightCache = new FootTargetCache();
    private readonly FootSmoother leftSmoother = new FootSmoother(), rightSmoother = new FootSmoother();
    private readonly PelvisController pelvisController = new PelvisController();
    private static readonly ProfilerMarker Marker = new ProfilerMarker("StepIK.ProceduralUpdate");
    private readonly PoseBackup pose = new PoseBackup();
    private Vector3 lastPosition;
    private TestMode previousMode;
    private bool previousAdaptive;
    private bool reportedError;
    private bool ownsAnimatorFootGoals;

    private void OnEnable()
    {
        AutoAssignHumanoidBones();
        settings.Clamp();
        ResetRuntime();
    }

    private void OnValidate()
    {
        settings?.Clamp();
        // Inspector edits to sampling settings must not reuse old surface data.
        leftCache.Invalidate(); rightCache.Invalidate();
    }

    [ContextMenu("Auto Assign Humanoid Bones")]
    public void AutoAssignHumanoidBones()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!animator || !animator.isHuman || !animator.avatar || !animator.avatar.isValid) return;
        if (!pelvis) pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
        Assign(leftLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        Assign(rightLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
    }

    private void Assign(LegReferences leg, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
    {
        if (!leg.upperLeg) leg.upperLeg = animator.GetBoneTransform(upper);
        if (!leg.lowerLeg) leg.lowerLeg = animator.GetBoneTransform(lower);
        if (!leg.foot) leg.foot = animator.GetBoneTransform(foot);
    }

    /// <summary>
    /// Binds an upright Humanoid in its neutral standing pose. The Animator, clips and root-motion
    /// settings remain owned by the caller. Use explicit contact weights when the gait provides them.
    /// </summary>
    public bool TryBindHumanoid(Animator source, LayerMask terrainMask, float solePlaneY, out string error)
    {
        if (!source || source.gameObject != gameObject)
        {
            error = "Put StepIKController on the same GameObject as the Animator.";
            return false;
        }
        if (!source.enabled || !source.runtimeAnimatorController ||
            source.updateMode != AnimatorUpdateMode.Normal || source.cullingMode != AnimatorCullingMode.AlwaysAnimate)
        {
            error = "Use an enabled Animator with a controller, Normal update mode and Always Animate culling.";
            return false;
        }
        if (terrainMask.value == 0)
        {
            error = "Select a terrain layer that contains the walkable colliders.";
            return false;
        }
        if (!StepIKHumanoidCalibration.TryMeasure(source, solePlaneY, out var hips, out var left,
            out var right, out error)) return false;

        pose.Restore();
        animator = source;
        pelvis = hips;
        leftLeg = left;
        rightLeg = right;
        if (settings == null) settings = new IKSettings();
        settings.terrainMask = terrainMask;
        settings.Clamp();
        ResetRuntime();
        return true;
    }

    /// <summary>Sets gait contact weights from 0 for swing to 1 for stance.</summary>
    public void SetContactWeights(float left, float right)
    {
        leftLeg.contactWeight = float.IsNaN(left) ? 0f : Mathf.Clamp01(left);
        rightLeg.contactWeight = float.IsNaN(right) ? 0f : Mathf.Clamp01(right);
    }

    // Restore last frame's pre-correction locals BEFORE Animator evaluation.
    // This prevents corrections accumulating on bones not keyed by the current clip.
    private void Update() { pose.Restore(); }
    private void OnDisable() { pose.Restore(); ResetRuntime(); }

    public void StartSimulation() { pose.Restore(); ResetRuntime(); BuiltInCallbackSeen = false; isRunning = true; }
    public void EndSimulation() { isRunning = false; pose.Restore(); ResetRuntime(); }
    public void SetMode(TestMode mode)
    {
        pose.Restore();
        if (testMode != mode) BuiltInCallbackSeen = false;
        testMode = mode;
        ResetRuntime();
    }

    public void ResetRuntime()
    {
        leftCache.Invalidate(); rightCache.Invalidate();
        leftSmoother.Reset(); rightSmoother.Reset(); pelvisController.Reset();
        lastPosition = transform.position;
        previousMode = testMode; previousAdaptive = adaptiveUpdates;
        Telemetry = default;
    }

    private bool ValidRig()
    {
        return animator && animator.enabled && animator.runtimeAnimatorController &&
            animator.updateMode == AnimatorUpdateMode.Normal &&
            animator.cullingMode == AnimatorCullingMode.AlwaysAnimate && pelvis &&
            leftLeg.IsValid && rightLeg.IsValid &&
            leftLeg.upperLeg.IsChildOf(pelvis) && rightLeg.upperLeg.IsChildOf(pelvis) &&
            leftLeg.upperLeg != rightLeg.upperLeg && leftLeg.foot != rightLeg.foot &&
            settings.terrainMask.value != 0;
    }

    private void LateUpdate()
    {
        if (testMode == TestMode.UnityAnimatorIK) return; // Built-in IK runs in its required callback.
        if (!isRunning || testMode == TestMode.FKOnly)
        {
            if (leftLeg.foot) LeftBaseRotation = leftLeg.foot.rotation;
            if (rightLeg.foot) RightBaseRotation = rightLeg.foot.rotation;
            ResetRuntime(); return;
        }
        if (!ValidRig())
        {
            if (!reportedError) Debug.LogWarning("STEP-IK: assign Animator, pelvis, both leg chains and a ground LayerMask. Animator requires Normal update and Always Animate culling.", this);
            reportedError = true; ResetRuntime(); return;
        }
        reportedError = false;
        if (previousMode != testMode || previousAdaptive != adaptiveUpdates) ResetRuntime();
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var telemetry = new IKTelemetry();
        using (Marker.Auto())
        {
            bool useCache = testMode == TestMode.StepIK && adaptiveUpdates;
            float speed = 0f;
            float sampleTime = 0f;
            if (useCache)
            {
                Vector3 position = transform.position;
                speed = Vector3.Distance(position, lastPosition) / dt;
                lastPosition = position;
                sampleTime = Time.time;
            }
            pose.Capture(pelvis, leftLeg, rightLeg);
            Vector3 leftBase = leftLeg.foot.position, rightBase = rightLeg.foot.position;
            LeftBaseRotation = leftLeg.foot.rotation; RightBaseRotation = rightLeg.foot.rotation;
            var left = Evaluate(leftLeg, leftCache, leftSmoother, useCache, speed, sampleTime, dt, ref telemetry);
            var right = Evaluate(rightLeg, rightCache, rightSmoother, useCache, speed, sampleTime, dt, ref telemetry);
            float drop = pelvisController.Step(left, right, leftBase.y, rightBase.y, settings, dt);
            pelvis.position += Vector3.up * drop;
            Vector3 forward = transform.forward;
            if (testMode == TestMode.FabrikIK)
            {
                FabrikIKSolver.Apply(leftLeg, left, forward, fabrikIterations);
                FabrikIKSolver.Apply(rightLeg, right, forward, fabrikIterations);
            }
            else if (testMode == TestMode.CcdIK)
            {
                CcdIKSolver.Apply(leftLeg, left, forward, ccdIterations);
                CcdIKSolver.Apply(rightLeg, right, forward, ccdIterations);
            }
            else
            {
                if (testMode == TestMode.StepIK)
                {
                    left = TerrainDetector.PreserveClearance(left, leftLeg);
                    right = TerrainDetector.PreserveClearance(right, rightLeg);
                }
                TwoBoneIKSolver.Apply(leftLeg, left, forward);
                TwoBoneIKSolver.Apply(rightLeg, right, forward);
            }
            telemetry.leftSupported = left.supported; telemetry.rightSupported = right.supported;
            telemetry.pelvisOffset = drop;
        }
        telemetry.scriptMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Telemetry = telemetry;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !animator || !animator.isHuman) return;
        if (!isRunning || testMode != TestMode.UnityAnimatorIK || !ValidRig())
        {
            // Release our previous goals once. In other modes leave the Animator's goals alone.
            if (ownsAnimatorFootGoals)
            {
                ClearAnimatorFootGoals();
                ownsAnimatorFootGoals = false;
            }
            return;
        }
        if (previousMode != testMode || previousAdaptive != adaptiveUpdates) ResetRuntime();
        BuiltInCallbackSeen = true;
        ClearAnimatorFootGoals();
        ownsAnimatorFootGoals = true;
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            Telemetry = default;
            return;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var telemetry = new IKTelemetry();
        LeftBaseRotation = leftLeg.foot.rotation; RightBaseRotation = rightLeg.foot.rotation;
        Vector3 leftBase = leftLeg.foot.position, rightBase = rightLeg.foot.position;
        var left = Evaluate(leftLeg, leftCache, leftSmoother, false, 0f, 0f, dt, ref telemetry);
        var right = Evaluate(rightLeg, rightCache, rightSmoother, false, 0f, 0f, dt, ref telemetry);
        float drop = pelvisController.Step(left, right, leftBase.y, rightBase.y, settings, dt);
        animator.bodyPosition += Vector3.up * drop;
        ApplyAnimatorGoal(leftLeg, left, AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee);
        ApplyAnimatorGoal(rightLeg, right, AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee);
        telemetry.leftSupported = left.supported; telemetry.rightSupported = right.supported;
        telemetry.pelvisOffset = drop;
        // Unity's native IK solve happens AFTER this callback; it is not included here.
        telemetry.scriptMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Telemetry = telemetry;
    }

    private void ClearAnimatorFootGoals()
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
    }

    private void ApplyAnimatorGoal(LegReferences leg, FootTarget target, AvatarIKGoal goal, AvatarIKHint hint)
    {
        animator.SetIKPositionWeight(goal, target.weight);
        animator.SetIKRotationWeight(goal, target.weight);
        if (target.weight <= 0f) return;
        // Avatar goals have their own reference frame, which can differ from the imported ankle axes.
        Quaternion delta = target.rotation * Quaternion.Inverse(leg.foot.rotation);
        Vector3 goalOffset = animator.GetIKPosition(goal) - leg.foot.position;
        animator.SetIKPosition(goal, target.position + delta * goalOffset);
        animator.SetIKRotation(goal, delta * animator.GetIKRotation(goal));
        if (leg.kneeHint)
        {
            animator.SetIKHintPositionWeight(hint, target.weight);
            animator.SetIKHintPosition(hint, leg.kneeHint.position);
        }
    }

    private FootTarget Evaluate(LegReferences leg, FootTargetCache cache, FootSmoother smoother,
        bool useCache, float speed, float sampleTime, float dt, ref IKTelemetry telemetry)
    {
        Vector3 heelOrigin = leg.foot.TransformPoint(leg.heelOffset);
        Vector3 toeOrigin = leg.foot.TransformPoint(leg.toeOffset);
        bool single = testMode == TestMode.SingleRayIK || testMode == TestMode.UnityAnimatorIK ||
            testMode == TestMode.FabrikIK || testMode == TestMode.CcdIK;
        Quaternion footRotation = leg.foot.rotation;
        bool reuse = useCache && speed < settings.fullUpdateSpeed &&
            cache.CanReuse(heelOrigin, toeOrigin, footRotation, sampleTime, settings);
        FootContact heel, toe;
        if (reuse)
        {
            heel = cache.Heel; toe = cache.Toe; telemetry.cacheReuses++;
        }
        else
        {
            heel = detector.Sample(single ? (heelOrigin + toeOrigin) * 0.5f : heelOrigin, transform, settings);
            toe = single ? default : detector.Sample(toeOrigin, transform, settings);
            telemetry.raycasts += single ? 1 : 2;
            if (useCache) cache.Store(heel, toe, heelOrigin, toeOrigin, footRotation, sampleTime);
        }
        var support = TerrainDetector.SelectSupport(heel, toe, settings);
        var target = leg.hasCalibratedRotation
            ? TerrainDetector.BuildCalibratedTarget(support, heel, toe, transform, leg, settings, single)
            : TerrainDetector.BuildTarget(support, leg.foot.position, footRotation, transform.up, leg, settings);
        if (useCache && !target.supported) cache.Invalidate();
        return smoother.Step(target, leg.foot.position, footRotation, settings, dt);
    }

    private sealed class PoseBackup
    {
        private readonly Transform[] bones = new Transform[7];
        private readonly Quaternion[] rotations = new Quaternion[7];
        private Vector3 pelvisLocalPosition;
        private bool saved;
        public void Capture(Transform hips, LegReferences l, LegReferences r)
        {
            bones[0] = hips; bones[1] = l.upperLeg; bones[2] = l.lowerLeg; bones[3] = l.foot;
            bones[4] = r.upperLeg; bones[5] = r.lowerLeg; bones[6] = r.foot;
            pelvisLocalPosition = hips.localPosition;
            for (int i = 0; i < bones.Length; i++) rotations[i] = bones[i].localRotation;
            saved = true;
        }
        public void Restore()
        {
            if (!saved) return;
            if (bones[0]) bones[0].localPosition = pelvisLocalPosition;
            for (int i = 0; i < bones.Length; i++) if (bones[i]) bones[i].localRotation = rotations[i];
            saved = false;
        }
    }
}
