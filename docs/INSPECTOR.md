# Structured model settings

## Install the Inspector

Copy `UnityAssets/Editor/TrackingControllerEditors.cs` into your Unity project's **Assets/Editor/** folder. It supplies the model component's collapsible menus; it is not a component to attach. Runtime controllers/helpers stay outside Editor. The Editor layout is excluded from player builds and adds no per-frame model work. Without this file Unity uses its ordinary flat Inspector, so copying only the runtime controllers does not install the new menus.

Existing runtime field names are retained so scene/prefab assignments and tuning survive. Foldout state is remembered in the Editor session, not serialized into the model. The UI supports normal serialized-property editing and Unity Undo. Optional features are grouped at the bottom and collapsed initially.

## Common terminology and units

- **Blend Rate (1/s):** 0 disables the low-pass stage; otherwise higher blends faster. Separate speed limits still apply. For the body this controls rest/loss transitions and legacy senders; live One Euro-filtered samples avoid a second fixed low-pass stage. Python landmark smoothing still lives in your tracking JSON.
- **Maximum Speed (deg/s):** caps the rotation rate. This is independent of the permitted angle/range and independent for arms, wrists, fingers and torso.
- **Dead Zone (deg):** ignores small angular target differences. Raising it reduces tiny tremors but can suppress intentional fine movement.
- **Maximum Rotation / Minimum Bend / Maximum Bend (deg):** pose limits relative to the captured rig reference. Wrist extension is negative and flexion positive. Finger maximum is a generic safety cone, not a fitted anatomical hinge model.
- **Hold Last Pose (s):** short missing-landmark hold while packets continue. **Packet Timeout (s)** instead handles missing packets.
- Eye texture travel uses **UV**, not degrees; sensitivity/input range uses iris ratios. The labels deliberately distinguish these quantities rather than pretending UV travel is an anatomical angle.

## Body component layout

```text
Connection & Confidence
Arms
  Response
  Limits
Wrists
  Response
  Limits
Fingers
  Response
  Limits
Torso
  Response
  Limits
Tracking Loss & Rest Pose (shared)
Optional Estimates — OFF by default
  Enable Optional Estimates
  Estimate Missing Arm from Hand
  Estimate Torso from Shoulders Only
  Estimated Depth Movement
Diagnostics
```

Arms, wrists, fingers and torso each have their speed and dead-zone settings near their limits. Loss/rest policy is shared and is shown once to avoid implying that four repeated copies are independent settings. New per-part rates start with the previous common defaults; existing fields retain saved values. You can now reduce finger speed/dead zone without changing the arms. The wrist keeps its slower 120 deg/s default while arm/finger/torso speed starts at the previous 720 deg/s cap.

The optional master is OFF even in an existing scene until explicitly enabled. Saved per-feature choices are retained but ignored with the master off. **Use Conservative Optional Defaults** also clears their toggles, disables depth movement and diagnostics, and leaves core response/limits/rest tuning untouched. Shoulder-only torso and hand-only arm estimates are not part of the normal tracking path. With these off, unreliable torso/arm data holds/returns instead of inventing a pose. Make shoulders/hips visible for full torso tracking.

The strict arm-requires-hand option and all automatic/manual crossing/interlacing pose guesses have been deleted. Normal arm tracking remains independent of fingers. Elbow clearance is a geometric safety limit, not an arm-crossing animation generator; it remains next to elbow bend limits.

## Face component layout

```text
Connection
Model References
Head
  Response
  Limits
  Axis Mapping & Torso Compensation
Eyes / Gaze
  Drive Method & References
  Response
  Limits
  Neutral Calibration
Blinking / Eye Confidence
Mouth
Optional / Diagnostics — OFF by default
```

Eye reference/limit controls depend on the selected drive method. Material Offset shows dedicated renderers and UV travel; Bone Rotation shows eyeball bones and angular limits. Automatic supports both, with bone references nested separately to reduce the earlier mesh/bone confusion. Preview eye sweep and automatic blinking are at the bottom. The conservative-defaults button disables them without changing blink thresholds, head directions, gaze sensitivity or material assignments.

For your flat eye meshes, choose Material Offset; a texture renderer is not a rotating eye bone. Head C calibration remains in Python, gaze G calibration remains in Unity; the neutral section also has a Play-mode gaze recalibration button. All existing input-backend compatibility is retained.

## Migration and verification

1. Stop Play, update Python/controller/helper files, and copy the new Editor layout to Assets/Editor.
2. Delete obsolete HandPosePreset.cs from Unity. If merging Python folders, delete tracking/overlap.py and tests/test_hand_overlap.py too. A fresh download avoids leaving deleted files behind. Keep your tuned JSON; removed overlap keys are ignored.
3. Expand Optional Estimates and confirm the master is OFF. Use the conservative-defaults buttons if you want to clear saved optional choices too.
4. Check each body part's Response/Limits in the new layout. Existing field names/settings are preserved; newly independent per-part fields use the defaults documented above.
5. Test normal tracking, lost tracking/rest, wrist limits, gaze and a player build. The editor script must stay in an Editor folder.

Optional development checks: copy UnityAssets/Tests/Editor into Assets/Editor and run **Tools > VTuber > Run Inspector settings regression checks** outside Play. These check serialized fields, optional reset behavior and preservation of core tuning. The body regression checks include independent finger rate limiting. These Unity checks and the visual Inspector have not been executed in the cloud; only source syntax and Python tests can be verified here.
