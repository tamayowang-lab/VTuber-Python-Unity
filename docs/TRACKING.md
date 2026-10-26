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

In Unity, import the existing UnityChan package as before. Copy **all five current runtime scripts** from `UnityAssets/Scripts/UnityChan3D/`:

- `HumanoidBodyController.cs`
- **`ArmRetargeting.cs` (required solver)**
- **`JointConstraints.cs` (required wrist-limit helper)**
- **`HandPosePreset.cs` (required overlap-preset data class)**
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

`Q` quits. `I` cycles automatic overlap, forced approximation, and off. `C` recalibrates optional depth; in main.py it also resets head neutral. `G` in Unity's focused Game window recalibrates gaze. See [EYES.md](EYES.md) for the current eye controls.

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

| Setting | Start with | Meaning |
|---|---:|---|
| Minimum Visibility | 0.6 | Gate pose/palm/finger data; predicted samples use reduced visibility |
| Maximum Angular Speed | 720 deg/s | Safety cap; old 240 value may noticeably limit fast arms |
| Maximum Finger Joint Rotation | 100 degrees | Bind-relative safety cone; not a calibrated hinge model |
| Rotation Dead Zone | 1 degree | Ignore tiny target changes; lower for fine finger motions |
| Smoothing / Finger Smoothing | 8 / 5 | Rest blending and old-sender fallback |
| Occlusion Grace Period | 0.4 seconds | Hold last local pose before rest |
| Return To Rest When Lost | checked | Otherwise hold indefinitely while packets continue |
| Require Hand For Arm Tracking | unchecked | Arm confidence is independent of detailed finger detection |
| Infer Arm From Hand | checked | Use bounded image-based inference only when measured arm is unreliable |
| Minimum / Maximum Elbow Bend | 15 / 150 degrees | Avoid locked straight arms and over-folding |
| Limit Elbow Inward | checked | Keep elbow outside torso-relative side plane |
| Elbow Side Clearance | 0.35 shoulder widths | Higher keeps elbows farther out |
| Resting Arm Side Angle / Elbow Bend | 18 / 18 degrees | More spacious relaxed rest, without altering bind reference |
| Depth Motion | Disabled | Optional stylized lean or whole-root movement, not absolute depth measurement |

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

Copy JointConstraints.cs into Assets alongside the existing controller/solver; do not attach this static helper as a component. Enable Limit Wrist Bend. Minimum Wrist Bend defaults to -40 degrees (extension) and Maximum Wrist Bend to +50 (flexion), relative to the captured neutral wrist. Try -25/+35 for tighter limits. The limit uses the neutral index-to-little palm axis expressed in the wrist parent's coordinates, not a hard-coded local X axis. Map the index/middle/little finger bases in the Humanoid Avatar; without a usable palm basis it skips the wrist bound rather than assuming an axis. Signed swing/twist decomposition leaves other wrist components available; this is a flexion/extension bound, not a forearm pronation or radial-deviation limit. It is enforced on the target, filtered pose, and held pose. Optional Unity Editor checks cover both signed bounds, twist preservation and rotated bind frames; they remain unrun here.

## Interlaced-hands / overlap approximation

This adds a cheap overlap mode, not a detector that can see hidden fingers. Show both hands separately first so temporal identities are established. Bring them together: body-wrist proximity relative to palm size starts an entry timer (0.12 seconds); a larger separation threshold and 0.2-second exit timer prevent flickering. A single duplicated hand cannot establish both identities. During overlap, candidate assignment stays one-to-one and uses body-wrist anchors/history. An unavailable hand may use its previously known palm shape translated to a still-visible body wrist, explicitly marked `overlap-held`. Finger data is withheld, not claimed as observed. If neither hand nor body wrists support the mode for 0.3 seconds, automatic overlap ends.

In Unity, Enable Interlaced Hand Pose is on by default. Overlap Finger Hold Seconds = 0.15 keeps the last reliable finger pose briefly; Overlap Blend Seconds = 0.3 blends into the per-hand fallback preset and back toward tracking/rest on separation. Preset curls are proximal/intermediate/distal flexion in degrees, using finger axes calibrated from the model's neutral palm. Left/Right Interlaced Hand presets can be tuned separately in the Inspector; all angles remain subject to existing finger safety bounds. Missing finger mappings are skipped. Confident wrists/palm bases can continue to move while hidden finger shapes are held/preset. The cached local-pose filter prevents Animator writes from restarting the blend. A stale connection releases the mode through the same fallback transition.

Copy the NEW HandPosePreset.cs into Assets beside JointConstraints.cs and ArmRetargeting.cs; none of these helpers is a component. Update HumanoidBodyController.cs plus the Python tracking/ directory, body_tracking.py, main.py together. Keep your tuned JSON values; older config files may omit the new overlap keys and receive their defaults, or merge the new keys from tracking.defaults.json. The optional wire `overlapActive` flag defaults to false for old senders. Existing JSON arrays and ports are unchanged.

Python overlap_mode can be `auto` (default), `off`, or `force`. Press I in the focused Python preview to cycle auto -> force -> off; debug overlay explicitly says OVERLAP approximation. Force is a deliberate animation cue, useful when occlusion prevents automatic entry; it does not fabricate measured hands or identify unseen fingers. To disable all overlap approximation, choose off in Python and/or uncheck Enable Interlaced Hand Pose in Unity. Python off restores the normal independent-hand validation path. If auto triggers during high-fives or other close-hand gestures, use off or cue force only when intended; this is overlap geometry, not a trained interlacing gesture recognizer.

The default per-hand curls are only a plausible hidden-finger fallback. They do NOT ensure that the avatar's hands physically meet, that fingers alternate exactly, or that meshes never intersect. Tune the two presets on the actual Unity-Chan rig. Hand contact still depends on arm proportions, IK wrist targets and elbow clearance. Do not remove joint safety limits to force an impossible pose. A fully authored interlaced pose or additional camera/hardware is needed for precise contact.

Python regression tests cover entry/exit hysteresis, single-hand duplicate rejection, visible body-wrist support during hand loss, separation recovery, swapped labels and explicit force/off modes. Optional Unity checks cover initial holding, preset convergence despite Animator writes, and no-snap recovery; these Unity runtime checks remain unrun here.
