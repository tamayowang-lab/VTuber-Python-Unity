# Current tracking and retargeting pipeline

This document replaces the incremental setup/tuning notes from older revisions.

## Assessment and design choices

The largest problems were structural, not just insufficient smoothing:

- Normal tracking aimed bones independently; hand-only tracking used a different solver. This changed the wrist trajectory when human/avatar segment proportions differed.
- The previous elbow correction projected the elbow and displaced the wrist even when an alternate elbow bend could preserve it. Separate tracking/inference corrections accumulated.
- Hand identity used only the current Holistic labels and a duplicate check. No identity history survived a short classification error.
- No adaptive landmark filter was active. Fixed-rate Unity smoothing and noisy image-space finger directions could add both jitter and latency. Earlier unused eye/mouth Kalman calculations consumed work without affecting outgoing values.
- Hand image coordinates and pose world coordinates have different units. Hand z is relative to its own wrist, not an absolute camera-space wrist target.
- Body mode always ran a second face model despite already using Holistic. Conversely, removing that second model entirely broke tight face-only crops because Holistic depends on a pose-derived face ROI.

The standalone body preview skips iris refinement; the full app reuses refined Holistic face points and lazily falls back to FaceMesh only on frames without a usable Holistic face. The implementation uses one vectorized One Euro stage, two temporal hand tracks, bounded translation-only wrist prediction and a shared analytic two-bone solver. No new inference model, Kalman dependency, Unity package or scene rig is required. Analytic IK is cheaper and easier to integrate with the current Unity 2020.3 Humanoid setup than introducing Animation Rigging constraints and another bone owner. The .unitypackage files contain assets/controllers, not a runnable Unity project in this checkout; actual Animator/Avatar behavior still needs Editor verification.

## Install and update

Use Python 3.10–3.12:

```sh
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
```

Copy the Python files AND the new `tracking/` directory to the laptop. Do not copy only `main.py`. Keep `body_depth.py`, `head_pose.py`, `facial_features.py`, `pose_estimator.py` and `model.txt` alongside it.

In Unity, import the existing UnityChan package as before. Copy **all four current runtime scripts** from `UnityAssets/Scripts/UnityChan3D/`:

- `HumanoidBodyController.cs`
- **`ArmRetargeting.cs` (required solver)**
- **`JointConstraints.cs` (required wrist-limit helper)**
- `UnityChanController.cs`

Replace matching scripts rather than adding duplicate classes. Keep the existing supporting UI/preferences/save scripts. No changes to the binary Unity packages or serialized scene are made automatically.

Use one Humanoid Animator with correctly mapped hips, spine, neck, upper/lower arms, hands and optional finger/eye bones. Chest/UpperChest mappings are optional. Bind directions and neutral rotations are captured at startup; they are not replaced with hard-coded bone axes. Start from the authored neutral/T-pose for calibration; the separate relaxed-arm fallback then lowers the arms. Disable root motion and competing arm/hand IK/LookAt scripts. Animator Normal mode is recommended. Body retargeting runs in LateUpdate at order 100; head/gaze runs afterwards at 200.

Start the scene and face TCP server, then:

```sh
python main.py --connect --body --tracking-config tracking.defaults.json --debug-tracking
```

Body TCP is loopback 5067, existing face TCP is 5066. Without Unity:

```sh
python body_tracking.py --tracking-config tracking.defaults.json --debug-tracking
python body_tracking.py --video recorded-upper-body.mp4 --debug-tracking
python body_tracking.py --video recorded-upper-body.mp4 --raw-tracking --debug-tracking
```

`Q` quits. The former I-key manual hand-pose cue has been removed. `C` recalibrates optional depth; in main.py it also resets head neutral. `G` in Unity's focused Game window recalibrates gaze. See [EYES.md](EYES.md) for the current eye controls.

## Coordinates and mirroring

1. Holistic receives the **unmirrored** BGR camera frame converted to RGB. Anatomical identity is not determined by which side of the preview a hand occupies. Holistic's semantic left/right is only a weak assignment prior; this API supplies no independent handedness probability.
2. Pose `pose_world_landmarks` are hip-relative meters. Hand and `imagePose` landmarks are image-relative; hand y is aspect-corrected before validation/filtering, not only when sending; their y is divided by image aspect so x/y/z directions use comparable image units.
3. Wire v1 retains x = -MediaPipe x, y = -MediaPipe y (aspect-corrected only for image data), z = MediaPipe z. Unity's existing **Invert Depth** option applies the final front/back sign exactly once. This deliberate preservation avoids silently breaking saved rigs. Its default is checked for this avatar; confirm forward reach before tuning filters. Never use hand-relative z as absolute wrist depth.
4. The face adapter applies both x reflection and a bijective canonical left/right index permutation (including iris centers), only to image-space face landmarks to match the original face pose calculation. The camera preview is mirrored for convenience; this does not relabel anatomical hands.
5. In Unity, shoulder-relative world wrist displacement is scaled once by avatar total arm length / measured total arm length. It is not assigned directly to bone positions. An elbow hint from the measured bend plane guides a fixed-length two-bone solution. A cached bend guide avoids unstable flips near straight arms.

Hand-only arm inference remains an approximation: image shoulders provide an anchor/scale, the hand wrist supplies x/y, and a bounded front-depth guess supplies the missing z. Without reliable anchors, hold/rest instead of inventing an arm.

## Identity, validation, filtering and loss

Order: detection → candidate validation → temporal assignment → per-finger/body validation → One Euro → optional brief prediction → wire conversion → shared IK → bounded bone rotations.

Assignment examines the two possible hand tracks and their recent wrist trajectories, body wrists, and (when needed) shoulder/elbow extrapolation. It solves the tiny assignment exhaustively; no general assignment library is needed. Duplicate skeletons cannot fill two identities. When assignments are indistinguishable, observations are withheld. History expires after 0.5 seconds, avoiding permanent attachment to an old location. Identity cannot be guaranteed when both hands overlap with no reliable body evidence.

One Euro updates use actual timestamps. `min_cutoff` controls stationary smoothing (lower = smoother), while `beta` increases responsiveness with speed (higher = less lag during fast motion). Each coordinate maintains its own derivative and filter state. Hands/fingers have separate parameters. `derivative_cutoff` (default 1 Hz) controls velocity-estimate smoothing; the shipped JSON exposes all these settings. Duplicate/reordered timestamps do not advance state, and long gaps reset filters instead of integrating stale derivatives. MediaPipe's extra pose smoothing is disabled to avoid cascaded filters. `--raw-tracking` disables One Euro but keeps identity and validation enabled.

World-pose visibility, finite coordinates, plausible arm lengths and timestamp-scaled jumps are checked separately. A consistent changed pose can be reacquired after three samples rather than permanently jump-rejected. Single-frame palm-normal flips are rejected when the palm plane is well-defined; smooth palm/back rotation is allowed. Finger chains with collapsed lengths or abrupt wrist-relative shape jumps are marked invisible individually; the palm and other fingers continue. Recovered fingers reset only their own filter state. Holistic has no per-finger confidence: geometric checks are a validity heuristic, not measured visibility or a reconstruction guarantee.

Brief missing hands may translate their last reliable shape using capped wrist velocity for **at most 0.08 seconds**. Finger curls are never extrapolated. Predicted output has reduced visibility. Longer loss sends no hand points, letting Unity hold briefly and then return to the natural pose. `trackingStatus` distinguishes missing, tracked/partial, predicted and rejected output; pose states distinguish low confidence, missing, nonfinite and implausible geometry. `observationStatus` records raw detector availability independently of prediction. Invalid landmarks are never silently promoted to reliable observations.

## Unity configuration

Existing Inspector values survive source updates; check these explicitly:

The component now uses an Editor-only foldout interface. Copy `UnityAssets/Editor/TrackingControllerEditors.cs` into **Assets/Editor/** to see the nested controls. See [Inspector layout and naming](INSPECTOR.md). Runtime field names are preserved for existing scene/prefab tuning.

| Part | Blend rate (1/s) | Maximum speed (deg/s) | Dead zone (deg) | Rotation/bend limits |
|---|---:|---:|---:|---|
| Arms | 8 | 720 | 1 | Elbow 15..150; optional inward clearance 0.35 shoulder widths |
| Wrists | 8 | 120 | 1 | Total 85; extension -40 / flexion +50 |
| Fingers | 5 | 720 | 1 | Joint safety cone 100 |
| Torso | 8 | 720 | 1 | Lean/turn 40 |

Shared hold-last-pose time is 0.4 seconds; packet timeout is 0.5 seconds. The relaxed rest pose uses arm side angle 18 degrees and elbow bend 18 degrees. Optional estimates have a new master switch defaulting OFF, so previous saved experimental flags cannot silently activate. Missing-arm inference, shoulder-only torso estimation, depth motion and diagnostic previews are at the bottom; use them only deliberately. Full torso tracking requires reliable shoulders and hips when shoulder-only estimation is off. The conservative-defaults button turns optional features off without changing core limits/smoothing.

New packets flag `adaptiveFiltered`. For measured poses from these packets Unity avoids a second fixed low-pass stage, retaining its angular speed bound and SLERP safety step. Rest transitions still advance from the cached local pose rather than the Animator's freshly-written T-pose. Old senders without the flag retain the existing Unity smoothing.

The shared IK solver first tries to preserve the wrist while choosing a safe elbow on its feasible bend circle. Joint bend limits preserve both avatar bone lengths. An impossible elbow-clearance target is nudged outward in at most five bounded attempts; failure holds/rests instead of stretching. This replaces the old independent elbow projection and separate inferred-arm solver. The post-filter safety check uses the same solver, not another set of joint rules. It is not full mesh collision detection; exact hand-to-face contact is not guaranteed.

Torso lean/twist uses available spine bones around an anchored pelvis. Shoulder-only fallback supports turns/side tilt when hips are hidden. Depth movement stays opt-in. Head/body compensation, relaxed wrists/open fingers, iris sensitivity/material gaze, both Unity input backends and save/load support remain available.

## Diagnostics and validation

`--debug-tracking` overlays raw landmarks in grey and the actual transmitted filtered landmarks in green/blue/orange. Text shows left/right track state, pose state and the actual body-wrist visibility values (not invented hand confidence). Lost/unreliable points are withheld rather than displayed as real observations. Select the avatar in Unity's Scene view with **Draw Tracking Gizmos** checked to see solved shoulder/elbow/wrist targets. Left/Right Arm Inferred indicators distinguish hand-only fallback.

Automated Python scenarios include stationary noise, fast movement response, variable frame rates, label swaps, only-left/right, temporary wrist-confidence loss, duplicated hand detection, palm-normal rotation through sideways/back views, finite/nonfinite finger outliers, recovery, brief loss/prediction expiry, world-pose jumps, wire units and face mirroring. These are synthetic logic tests, not a claim that every real palm/back view will detect correctly.

Optional Unity checks: copy `UnityAssets/Tests/Editor/` into `Assets/Editor/`. Outside Play choose **Tools → VTuber → Run body controller regression checks**, and the separate eye checks. These exercise the actual IK solver, proportions, elbow clearance, bind-independent target generation, rest/hold transitions and gaze plumbing. Unity is unavailable in the cloud; these C# runtime checks have not been run here. C# syntax parsing alone is not compilation or rendered-model validation.

Manual camera/Editor checks still required:

1. Face straight, press C, test forward reach before changing sensitivity; the avatar must not reach backward.
2. Hold both hands still, then move arms quickly. Compare `--raw-tracking` with adaptive filtering; choose beta rather than adding another smoothing layer.
3. Show only one hand, flip palm/back/sideways, cross hands, briefly hide each hand, then return. Watch state overlays and avoid treating predicted points as detections.
4. Inject/observe a fingertip jump: only that finger should relax; a bad palm should hold/relax the hand without driving an impossible arm.
5. Test wrists near the face, arms crossing the chest and nearly straight elbows; inspect solved gizmos and confirm no bone stretching. Contact with the face is approximate.
6. Stop/restart Python and test lost tracking, finger recovery, torso lean and opt-in depth. Check idle arms remain calm and existing blink/mouth/gaze controls still work.

The real-face reuse parity test also compares the adapter against the original mirrored FaceMesh on the bundled demo crop (about 1.35 pixels RMS in this run).

Current-instance evidence: blank-frame CPU inference, 33-pose-landmark person-image inference, and 478-face-landmark fallback on a tight demo webcam crop were exercised. A 400-frame synthetic temporal-processing benchmark measured about 1.3 ms mean / 2.1 ms p95 on this machine, **excluding inference, drawing, camera and Unity**. No end-to-end FPS or live-camera latency is claimed.

## Wrist bend bounds

Copy JointConstraints.cs into Assets alongside the existing controller/solver; do not attach this static helper as a component. Enable Limit Wrist Bend. Minimum Wrist Bend defaults to -40 degrees (extension) and Maximum Wrist Bend to +50 (flexion), relative to the captured neutral wrist. Try -25/+35 for tighter limits. The limit uses the neutral index-to-little palm axis expressed in the wrist parent's coordinates, not a hard-coded local X axis. Map the index/middle/little finger bases in the Humanoid Avatar; without a usable palm basis it skips the wrist bound rather than assuming an axis. Signed swing/twist decomposition applies Min/Max only to flexion/extension. The separate Maximum Wrist Rotation safety cone bounds combined wrist twist/deviation; actual forearm pronation is a parent-bone motion and is not reconstructed accurately by unrestricted wrist rotation. It is enforced on the target, filtered pose, and held pose. Optional Unity Editor checks cover both signed bounds, twist preservation and rotated bind frames; they remain unrun here.

## Removed crossing/interlacing guesses

The automatic and manual interlaced-hand presets, proximity heuristics, I-key cue, strict Require Hand For Arm Tracking option, and their runtime fields/helper code have been removed. Tracking valid arms never requires visible fingers. Normal arm IK, elbow clearance, independent finger validation, bounded short wrist prediction and the hold/return policy remain. The old JSON overlap keys are ignored solely for migration; they cannot activate a guessed pose. Your One Euro tuning is preserved.

When updating an existing Unity project, delete the obsolete `HandPosePreset.cs` file. Remove the obsolete Python `tracking/overlap.py` and old `tests/test_hand_overlap.py` if merging folders, or extract a fresh checkout and copy your tuned JSON. These files are absent from the new download. Update the runtime controllers, helpers, and optional Editor regression checks together.

## Wrist stability and head neutral

Arm bone orientation now uses a continuous IK bend-plane frame rather than direction-only FromToRotation, preventing its antiparallel twist ambiguity from rotating the wrist parent. Wrist min/max flexion alone did not constrain twist, and the global 720 deg/s arm cap was too permissive for noisy palm normals. Wrists now have Maximum Wrist Rotation = 85 degrees around their neutral pose and Maximum Wrist Angular Speed = 120 deg/s, separate from arm speed. Keep Limit Wrist Bend enabled for the flexion plus total-rotation safety bounds. Nearly edge-on/degenerate palm frames are rejected for wrist orientation. Valid individual fingers continue independently; an uncertain palm normal does not replace all visible finger data. These bounds can restrict true palm/back rotation because full forearm pronation is not reconstructed; raising limits indiscriminately brings the instability back. Test 120-180 deg/s wrist speed, keeping arm speed 720. Optional Editor checks cover total rotation and per-wrist rate limiting.

Head neutral previously captured one face sample, potentially locking in an initially tilted/noisy estimate. It now averages 12 stable rotation matrices on SO(3); moving more than 8 degrees restarts that window. The preview asks you to hold straight during calibration. C resets the window. `--head-calibration-frames 1` explicitly restores single-frame calibration if needed, but is more vulnerable to bias. At 30 FPS the default needs about 0.4 seconds (longer at lower FPS). This cannot establish gravity/upright from an arbitrary camera or fix an authored tilted neck pose. If the avatar tilts before Python connects, inspect its neutral neck/Animator pose; if only after connecting, calibrate upright and inspect reported roll. G still calibrates eyes, not head.

## Helper organization and contact limits

Keep helpers separated by responsibility: ArmRetargeting solves IK and JointConstraints bounds joints; HumanoidBodyController owns the runtime update. They compile together and separate source files do not add per-frame Update calls or meaningful CPU overhead. Do not make helpers separate MonoBehaviour components or split every small method into its own file. Current helpers should remain beside their controller in Assets.

A standard webcam does not provide reliable absolute hand-to-face distance. The body pose is hip-relative, hand z is wrist-relative, and avatar proportions differ. A hand remaining away from the cheek is therefore not solved by overlap presets or global smoothing. A separate contact-aware wrist-target IK feature could approximate touching, but it would be an intentional interaction constraint, not recovered depth. It is not implemented by this update. Do not scale bones or remove safety limits to force contact.
