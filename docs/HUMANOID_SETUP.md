# Use STEP-IK on another character

STEP-IK means **Surface Tracking for Enhanced Placement with Inverse Kinematics**. It is a Unity component with a C# API. The character's Animator and movement controller still own the walk animation and locomotion. STEP-IK adjusts foot placement after evaluation of the authored pose.

For a separate Unity 6 project, import the STEP-IK Unity API package via **Assets → Import Package → Custom Package**. You can also copy the `Core` folder into `Assets` and copy `Editor/StepIKHumanoidSetupMenu.cs` into an `Editor` folder for the setup menu and probe gizmos. The core does not require the sandbox scene, benchmark UI, or City People models.

## Set up a humanoid from the Hierarchy

1. Import a character using **Rig → Animation Type → Humanoid** and configure its Avatar. It needs exposed hips, thighs, calves, and feet. If the hips or legs are unavailable, check **Optimize Game Objects** in the import settings. Assign your existing Animator Controller and check the locomotion clip. [Unity’s Humanoid rig import documentation](https://docs.unity3d.com/Manual/FBXImporter-Rig.html)
2. In Edit Mode, put the character upright in a neutral standing pose at its intended scale, with both feet level. Use a uniform positive scale. Select the character in the Hierarchy or open its prefab in Prefab Mode.
3. Run **Tools → STEP-IK → Set Up Selected Humanoid**. The menu binds the skeleton, estimates the heel and toe probes, and sets the Animator to **Normal** updates and **Always Animate** culling. The Avatar, Animator Controller, clips, and root motion settings stay assigned. Undo reverts the setup.
4. In **Step IK Controller → Settings**, check **Terrain Mask**. The menu initially selects the **Ground** layer if it exists and otherwise selects **Default**. The layer must contain enabled walkable colliders and should exclude character colliders.
5. With the character selected and Scene view **Gizmos** on, check that the teal heel and amber toe markers sit under the intended sole points. If footwear or a raised root pivot makes the estimates inaccurate, edit each leg's foot-local **Heel Offset** and **Toe Offset**. Run **Validate Selected Humanoid** to check assignments, then compare FK and STEP-IK on a level collider before trying slopes or stairs.

Use one foot IK system for the same bones. In STEP-IK mode, disable conflicting foot IK scripts, state Foot IK, or Animation Rigging constraints that write the legs. A humanoid with generic rigs, tilted roots, or nonuniform scaling needs separate integration and calibration.

## C# setup

Use this pattern at spawn time while the humanoid is in its upright neutral pose. Pass the world Y of the sole plane for that pose. On a model with its root at ground level, this is usually `animator.transform.position.y`. Configure the Animator's update and culling modes before binding.

```csharp
var ik = animator.GetComponent<StepIKController>();
if (ik == null)
    ik = animator.gameObject.AddComponent<StepIKController>();

if (!ik.TryBindHumanoid(animator, walkableLayers, neutralSoleY, out var error))
{
    ik.EndSimulation();
    Debug.LogWarning(error, animator);
    return;
}

ik.SetMode(StepIK.TestMode.StepIK);
ik.StartSimulation();
```

| API | Use |
| --- | --- |
| `TryBindHumanoid(animator, mask, neutralSoleY, out error)` | Maps legs, estimates sole probes, assigns terrain mask, and reports unsupported setups. |
| `SetContactWeights(left, right)` | Supplies gait weights from your movement or animation code, with 0 for swing and 1 for stance. Write these in Update before the leg solve. Set `settings.useHeightBasedSwingFade = false` when supplying phase weights explicitly. |
| `StartSimulation()` / `EndSimulation()` | Starts or suspends correction. Suspend during jumps or while a different system controls the legs. |
| `SetMode(mode)` / `adaptiveUpdates` | Chooses the comparison mode or fixed/adaptive sampling. |
| `ResetRuntime()` | Clears cached ground contacts and filters after teleports or intentional rig changes. |
| `Telemetry` | Reports ground probe count, cache use, support, pelvis offset, and managed pipeline time. |

The calibration values are estimates because avatars map the skeleton rather than measuring the shoe mesh. If the rig already has tuned probe offsets, keep those values and call `ResetRuntime()` after changes to the settings.

Use `StepIKController` and the `StepIK` namespace for v0.7 integrations. When upgrading, update C# references and verify your character setup in the Inspector.
