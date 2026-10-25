# VTuber Python Unity Tutorial

An Implementation of VTuber (Both 3D and Live2D) using Python and Unity. Supporting **face movement tracking**, **eye blinking detection**, **iris detection and tracking** and **mouth movement tracking** using **CPU only**.

## Usage
![Live2D Demo](https://github.com/mmmmmm44/VTuber-Python-Unity/blob/main/Images/live2d_demo.gif)

*Live2D Demo*

![UnityChan 3D Demo](https://github.com/mmmmmm44/VTuber-Python-Unity/blob/main/Images/unitychan3d_demo.gif)

*UnityChan 3D Demo*

## Features
* Facial landmarks detection and movement tracking supported by Facemesh by [Mediapipe](https://github.com/google/mediapipe).
* Various facial expressions detection including eye blinking, iris and mouth movements.
* Running **smooth 30 FPS** with **CPU only** for the aformentioned features.
* Simple and clean UI for adjusting the sensibility of detection in Unity.
* Saveload mechanism to save and load your preferences in Unity.
* Including sample (Unity) projects for both 3D and Live2D models.
* Support Window and M1 machines.
* Detailed and thorough explanation videos (with EN and ZH subtitles) [playlist](https://www.youtube.com/playlist?list=PL624GOtU88c4J9rGy7ZWjeSkOvCvuavan)

## File Explanation
|File|Description|
|:---:|:---:|
|main.py|The main program|
|facial_landmark.py|The module which is used to detect your face and generate the facial landmarks.|
|pose_estimator.py|The module which estimates your pose/ orientation of your head based on the landmarks.|
|stabilizer.py|Implementation of Kalman Filter to stabilize the values.|
|facial_features.py|Various facial features detection implementation, including blinking, iris detection and mouth movement.|
|model.txt|The points of the 3D Canonical model used in Mediapipe. [Source file](https://github.com/google/mediapipe/blob/master/mediapipe/modules/face_geometry/data/canonical_face_model.obj)|
|UnityAssets|Whole Unity Projects (in packages) and Scripts (old version) for both 3D (UnityChan) and Live2D (Hiyori) models|



## Background
Using avatars for streaming, content creation and VR gaming has been gaining increasing popularity, especially the boom of Hololive and other related companies active apperances in social media platforms such as YouTube and Twitter. Curious about the technology behind, I create this project after multiple researches.

Existing projects rely on Dlib, which although providing reliable and accurate facial landmark detection, requires decent graphic cards to run. However, implemented with the recent FaceMesh model in [Mediapipe](https://github.com/google/mediapipe), accurate detection and tracking can be run smoothly using CPU only, making running on computers with mediocre graphic cards or laptops with integrated graphic cards possible.

## How To Use
Clone this project into your directory

```
git clone https://github.com/mmmmmm44/VTuber-Python-Unity.git
cd "VTuber-Python-Unity"
```

### Setup
1. Create An empty Unity 3D Project

2. Import either the Live2D or UnityChan3D package to your project. The corresponding SDKs have been included already. (Excpet that I removed the voice files of UnityChan in the UnityChan Unity package due to file size limit in github.)

    - _last edit: 16-05-2022_

3. **[NEW]** In the Game window of Unity, create a 9:16 portrait ratio display mode and save it. Then the UI will return normal. 
(Or you can adjust the ratio in the AspectGridLayoutCellSize.cs attached on each of the panel Game Object, namely "Setting Panel" and "TCP Setting Panel".) [Tutorial](https://github.com/mmmmmm44/VTuber-Python-Unity/issues/14)

3. Run the Scene. Click the Setting button to switch on the TCP server. There should be a "Waiting for connection..." log message showing in the Console of Unity.

4. Run the following code in terminal
[content in the bracket is optional]
```
python main.py --connect [--debug] [--port PORT]
```

5. Enjoy


### Custom Setup (YT Tutorial)

#### For Live2D model

Video Walkthrough: [Click Me!](https://youtu.be/3pBc9Wkzzos?t=30)

1. Download the Cubism SDK For Unity from this [website](https://www.live2d.com/download/cubism-sdk/) and the sample model used (桃瀬ひより) from this [website](https://www.live2d.com/download/sample-data/)

2. Create an empty Unity 3D project, and import the Cubism SDK. Unzip the model and drag the whole folder to the Project window of the Unity Project.

3. Drag the live2D model's prefab into the scene. Run the scene immediately to allow the model to be shown in Scene and Game window.

4. Adjust the camera's position, background and projection properties. If there are some werid projection problems of the model, changing the projection of the camera from Perspective and Orthographic works for me.

5. Drag the HiyoriController.cs to the Hiyori GameObject. Adjust the parameters in the inspector

6. Run the scene.

7. Run the following code in terminal
[content in the bracket is optional]
```
python main.py --connect [--debug] [--port PORT]
```

8. Enjoy

#### For 3D Model (UnityChan)

Video Walkthrough: [Click Me!](https://youtu.be/V6Wd2kPNbPY?t=180)

1. Download the UnityChan model from the [website](https://unity-chan.com/). Go to "Data Download", accept the terms and agreements, and select the first one. Unzip the file.

2. Create an empty Unity 3D Project. Drag the unzipped folder to the Project Window of the project.

3. Go to UnityChan\Prefabs and Drag the "unitychan" prefab into the scene.

4. Adjust the camera's position, background and field of view.

5. Drag the UnityChanControl.cs script onto the prefab. __Change the update mode of the Animator attached to "Animate Physics" and the Controller to UnityChanLocomotions. (Crucial)__ Adjust the variables in the inspector. Disable other attached scripts except AutoBlink and UnityChanController. You may tick the box "is Auto Blink Active" in UnityChanContoller to enable auto blinking (enable AutoBlink script when ticked).

6. Run the scene first

7. Run the following code in terminal
[content in the bracket is optional]
```
python main.py --connect [--debug] [--port PORT]
```

8. Enjoy

**Make sure you run the Unity Scene first before running the python script**

_The complete Unity Project with fancy stuffs such as UI system, Save Load Preferences, and custom port UI can be found after importing the Unity Packages provided._

## Options (main.py)
    -h, --help                           show this help message and exit

    --connect                            connect to the unity character

    --port PORT                          specify the port of the connection to 
                                         unity. Have to be the same as in Unity.

    --cam CAM                            specify the camera number to use 
                                         if you have multiple cameras connected
                                         to the computer.
                                         (for cv2.VideoCapture(CAM))

    --debug                              show the raw values of the detection 
                                         in the terminal

## Examples

Connecting to the Unity Project with default port, showing the sent data.
    
    python main.py --connect --debug

Connecting to the Unity Project with custom port (e.g. 5077), showing the sent data.

    python main.py --connect --debug --port 5077

## Development Environment
* Python 3.8.5
* Numpy 1.19.2
* OpenCV 4.5.1
* Mediapipe 0.8.9.1

* Unity 2020.3.12f1

(Later version should be supported as well)

*(For Windows, it is recommended to run this project using Anaconda and create a virtual environment before installing such packages.)*

*The whole project is run on a laptop with Intel Core i5-8250U, with 16GB RAM and integrated graphic card only.*

*The same project is tested on a M1 Max device, running in rosetta.*


## References/ Credits

[Detect 468 Face Landmarks in Real-time | OpenCV Python | Computer Vision - Murtaza's Workshop - Robotics and AI](https://youtu.be/V9bzew8A1tc)

[Eye motion tracking - Opencv with Python - Pysoruce](https://youtu.be/kbdbZFT9NQI)

 | Project | Author | LICENSE |
 |:---:|:---:|:---:|
 | [head-pose-estimation](https://github.com/yinguobing/head-pose-estimation) | [Yin Guobing](https://github.com/yinguobing) | [LICENSE](https://github.com/yinguobing/head-pose-estimation/blob/master/LICENSE) |
 | [VTuber_Unity](https://github.com/kwea123/VTuber_Unity) | [AI葵](https://github.com/kwea123) | [LICENSE](https://github.com/kwea123/VTuber_Unity/blob/master/LICENSE) |
 |[VTuber-MomoseHiyori](https://github.com/KennardWang/VTuber-MomoseHiyori) |[KennardWang](https://github.com/KennardWang)|[LICENSE](https://github.com/KennardWang/VTuber-MomoseHiyori/blob/master/LICENSE)|

Hiyori Momose's model
|Position|Creator|
|:---:|:---:|
|Illustration|Kani Biimu [Twitter [@kani_biimu](https://twitter.com/kani_biimu)]|
|Modeling|Live2D Inc.|

## License
MIT

The Unity Chan model in the Unity Packages provided is distributed under Unity-Chan License © Unity Technologies Japan/UCL. A seperate sets of that License is included in UnityAssets/Licenses/UCL2_0

## Torso, arm and finger tracking (3D Humanoid avatars)

Install Python 3.10–3.12 and the compatible dependencies:

```sh
python -m pip install -r requirements.txt
```

1. Import the UnityChan package, or use another model with its Rig set to **Humanoid** and a valid Avatar. Check that shoulders, arms, hands and all finger bones are correctly mapped in Configure Avatar.
2. Copy `UnityAssets/Scripts/UnityChan3D/HumanoidBodyController.cs` into your Unity project's Assets folder. The existing `.unitypackage` files do not contain this new script.
3. Attach `HumanoidBodyController` to the same GameObject as the model's Animator. Keep the existing face controller for head, eyes and mouth. Start with the model in its neutral T-pose: the new component records the rig's initial bone directions. Disable competing arm/hand IK, animation scripts and root motion; use the Animator's **Normal** update mode. The body component applies rotations in `LateUpdate`.
4. Run the scene. Enable the existing face TCP server on port **5066**. The body component starts its own loopback TCP listener on **5067**.
5. On the same camera-equipped computer, run:

```sh
python main.py --connect --body --debug
# Custom ports must match both Unity components:
python main.py --connect --body --port 5066 --body-port 5067 --cam 0
```

Without `--connect`, `python main.py --body --debug` detects landmarks locally. Without `--body`, the original face-only workflow is retained, including Live2D compatibility. Use an unmirrored camera feed: body tracking assigns anatomical left/right and converts coordinates to an avatar facing the camera.

Keep hips, shoulders, elbows and hands visible, with enough light and space between fingers. Tracking drives hip/torso orientation, both arms, palm orientation, and three segments of each finger. Missing finger bones are skipped. Tune **Minimum Visibility**, **Smoothing** (higher follows faster), and **Tracking Timeout** in the Inspector. Occluded limbs return toward the initial pose; before the first packet and after a connection timeout, the body controller leaves the bones to the Animator instead of repeatedly forcing a rest pose. The face and body streams are independent, so body updates continue when no face is found.

This is monocular motion estimation: hand depth, occlusion and fast movements can be inaccurate. It does not implement walking, foot IK, global body translation, or precise anatomical joint limits. Body/hand inference adds CPU cost. Avatar-specific visual tuning must be checked in the Unity Editor.

The additional protocol is newline-delimited JSON (`version: 1`), containing `pose` (33 world landmarks), `leftHand` and `rightHand` (21 landmarks each). Each point has `x`, `y`, `z`, `visibility`; an empty array means tracking was lost. Pose coordinates are in meters; hands use image-relative coordinates with aspect correction, so only their directions are used. The receiver handles split/coalesced TCP reads, validates frames, and applies bone changes on Unity's main thread. The existing face protocol is unchanged.

Run the Python regression checks from the repository root:

```sh
python -m unittest discover -s tests -v
```

### Real-time body and hand preview

For a standalone camera view, like `facial_landmark.py`, run:

```sh
python body_tracking.py
# Select a different camera:
python body_tracking.py --cam 1
```

Or use `python main.py --body` to overlay body and hands on the existing face preview. Add `--connect` to also drive Unity. Both previews are mirrored, with green pose connections, blue left-hand connections and orange right-hand connections. Each detected hand displays its 21 landmarks and finger connections. Press **Q** to quit. No Unity connection is required to preview detections.

If blue and orange hand skeletons overlap on one hand, a duplicate filter now compares the full hand shapes and checks the body wrist positions. It keeps the hand supported by a visible wrist; ambiguous duplicates are hidden from both the preview and Unity stream. Keep shoulders and wrists visible for reliable assignment. Two genuinely overlapping hands can also be suppressed; this filter reduces duplicate detections but does not guarantee handedness during occlusion.

If the head flips when entering Play mode, replace the old UnityChanController.cs with the updated file in UnityAssets/Scripts/UnityChan3D (replace its contents; do not add a second class). It now records the neutral neck pose instead of applying a fixed rotation. Head offsets are applied in avatar axes relative to the neck parent after animation, allowing the head to follow the tracked torso. Start with a neutral neck pose. In Unity, check Play without Python first, then connect and check head yaw/pitch/roll and torso movement. This fix requires visual verification in the Unity Editor.

Direction and jitter tuning: the updated UnityChanController has Invert Pitch enabled by default, correcting reversed nodding without changing mirrored yaw. HumanoidBodyController has Invert Depth enabled by default, reversing incoming camera z for torso, arms and hands together. If your avatar uses a different facing convention, toggle that option and test a forward reach. For shaky arms, start with Smoothing = 8, Maximum Angular Speed = 240, Rotation Dead Zone = 1, and Occlusion Grace Period = 0.2. Higher smoothing follows faster; a larger dead zone suppresses small motion. Brief missing landmarks hold the last pose before returning to rest. These reduce jitter but cannot resolve all monocular depth errors. Disable competing arm IK/controllers and use Animator Normal mode. No arm SkinnedMeshRenderer reference is required: arm and finger movement is driven through Humanoid bones.

No-input arm shaking: replace HumanoidBodyController.cs with the latest version. Previously its no-data branch reset every bone in LateUpdate, which could fight Animator/IK even before Python connected. It now performs no bone writes before the first packet or after Tracking Timeout, and initializes its smoothing state from the current pose when data resumes. To verify, stop Python, enter Play, and compare with the body component disabled. If shaking persists with the component disabled, inspect Animator clips, arm IK and other bone controllers. During active tracking, competing arm animation/IK should still be disabled. The no-input fix has been reviewed in source but requires Unity runtime verification.

### Lost-hand stability and anchored torso

Use the updated HumanoidBodyController.cs AND UnityChanController.cs together.

- **Require Hand For Arm Tracking = true** (default): when the detailed hand disappears, stop trusting the pose detector's inferred wrist for that arm. Keep it false only if you want arm tracking without visible detailed hands.
- **Occlusion Grace Period = 0.4**, **Return To Rest When Lost = true**: hold the last reliable local limb pose briefly, then ease toward the captured starting pose. Set Return To Rest When Lost false to keep that last local pose indefinitely while packets continue. Held arms follow their moving parent torso. Old components may retain the previous 0.2 Inspector value; change it explicitly.
- The pose filter now advances from its own saved local rotation, including during return-to-rest; Animator writes no longer restart that transition each frame. This also keeps missing fingers stable. No bone writes occur before the first packet or after a full connection timeout.
- The hips stay at their starting local position and rotation during active tracking. Lean and twist are distributed across the mapped Spine, Chest and UpperChest bones. Chest/UpperChest are optional; Spine can target Neck directly. Keep shoulders and hips visible for full lean/twist estimation. Allow Shoulder Only Torso (enabled by default) supports turn and side tilt when hips are hidden, but cannot reliably infer forward lean. Disable it to require hip visibility. The Torso Tracking Inspector indicator shows when a usable torso estimate is present. This models upper-body lean/twist around a fixed pelvis, not walking or independent whole-body translation. Disable Animator root motion and competing body/arm IK.
- **Compensate Torso Motion = true** in UnityChanController: camera-relative head angles are applied after the torso, avoiding duplicated torso rotation in the head. Disable only if you explicitly want head angles relative to the animated chest.

Desktop verification: enter Play with Python stopped (no shaking), connect Python, raise an arm, hide that hand (brief hold then smooth return; other visible hand continues), and show it again (smooth reacquisition). Repeat with both hands hidden while leaning/twisting with hips visible. The pelvis should remain fixed while the spine moves; the head should keep its camera-relative orientation. Stop Python to check control is released to Animator; reconnect to check smooth initialization. These Unity runtime checks cannot be performed in the cloud environment.

Optional Unity regression checks: copy UnityAssets/Tests/Editor/BodyTrackingRegressionChecks.cs into Assets/Editor, with both controller scripts present in the project. Outside Play mode choose Tools > VTuber > Run body controller regression checks. This exercises the actual local-pose filter against simulated Animator overwrites, no-input/timeout ownership, held-parent motion, missing-hand arm gating, spine-only torso and shoulder-only fallback. These checks are supplied but have not been run here because Unity Editor is unavailable; they do not replace visual validation on the actual avatar.

### Updated neutral head angles and relaxed resting arms

Update main.py, the NEW head_pose.py module, and both Unity controller scripts together. The old Python calculation used Rodrigues-vector components as Euler angles and added 180 degrees before clipping pitch; that could saturate nodding at a constant angle. HeadOrientation now compares rotation matrices with a captured neutral orientation and extracts separate pitch, yaw and roll. Look straight ahead when your face is first detected. Press C in the focused Python preview to recapture neutral, then nod and turn to check the result.

For this new angle calculation, explicitly UNCHECK Invert Pitch and CHECK Invert Yaw in UnityChanController. Existing Inspector values survive script updates, so the old Invert Pitch setting must be changed manually. Invert Yaw reverses the earlier mirrored head-turn convention to match anatomical left/right arm control. Keep Compensate Torso Motion enabled. Head Smoothing = 10 is the default; 0 disables smoothing.

HumanoidBodyController now has Use Relaxed Arm Rest Pose enabled by default. It keeps the rig's original bind directions for retargeting but computes separate resting upper-arm/forearm rotations pointing down beside the hips, with a small outward angle and slight elbow bend. The idle and lost-tracking return uses this pose rather than the T-pose. Start with Resting Arm Side Angle = 8 and Resting Elbow Bend = 12; set these before Play. With this option enabled the controller intentionally owns the resting arms even before input and after timeout, using its persistent filter rather than repeatedly starting from the Animator. Disable the option to leave idle arms to the Animator. Return To Rest When Lost still controls hold-versus-return while tracking packets continue. The Python preview stays mirrored for convenience; that does not determine the Unity head-turn convention.

Python regression tests include positive/negative nods crossing the Rodrigues +/-180-degree boundary, independent yaw/roll, and neutral recalibration. The optional Unity Editor regression checks now also exercise relaxed arm targets while an Animator repeatedly supplies a T-pose. Unity runtime checks remain necessary on the actual model.

### Independent fingers, wider rest pose, and optional distance movement

Update body_tracking.py, the NEW body_depth.py module, main.py, and HumanoidBodyController.cs together. Existing head_pose.py and UnityChanController.cs remain required.

In the Body controller Inspector, UNCHECK Require Hand For Arm Tracking. Reliable shoulder/elbow/wrist landmarks then control each arm independently of detailed finger tracking. With hand landmarks missing, the wrist smoothly aligns to the forearm using its neutral local orientation and fingers return to the rig's open rest pose. If the arm itself is unreliable, the existing hold-then-return policy still applies. Palm orientation uses palm-base points; a collapsed/implausible finger chain falls back independently without disabling other fingers, the palm or the arm. Holistic does not provide per-finger confidence, so geometric checks cannot identify every occluded or noisy finger. Finger Smoothing = 5 reduces finger jitter separately from arm responsiveness.

Set Resting Arm Side Angle = 18 and Resting Elbow Bend = 18 before Play for a more spacious resting pose. The old Inspector values (8/12) survive source updates and must be changed explicitly. Wider arm placement does not modify the rig's retargeting bind pose.

Depth Motion offers Disabled, Upper Body Lean (default), and Whole Avatar. Upper Body Lean keeps the pelvis fixed and adds a bounded forward/backward lean to the upper body. Whole Avatar translates the model root along its initial forward axis, so the feet move too; disable root motion and competing root controllers. Use Whole Avatar if you want approaching/receding movement of the complete character. Default Maximum Depth Translation = 0.3 Unity units, Depth Translation Scale = 1, Maximum Depth Lean = 18 degrees. These gains are stylistic, not a metric measurement of your real movement.

The Python tracker estimates relative camera distance from apparent shoulder scale, calibrating on 12 reliable front-facing frames. Face the camera at your normal distance during calibration. Press C in the focused camera preview to recalibrate depth (and head neutral when using main.py). Move closer/farther slowly with shoulders visible. A fixed camera is required: camera zoom and some body rotations/shoulder movements can imitate distance changes. Large shoulder turns, hidden shoulders and small scale noise are rejected; Unity smooths unavailable depth toward neutral. Monocular MediaPipe world landmarks are hip-relative and cannot supply absolute camera distance by themselves. New optional depthValid/depthOffset packet fields carry this approximate cue; older packets safely supply no depth motion.

Python tests cover calibration, forward/backward signs, turning/visibility rejection and small scale noise. Optional Unity Editor checks cover independent arm tracking without hands, collapsed-finger isolation and bounded root motion in addition to the previous checks. Unity visual validation on the actual avatar is still required.

### Head tilt direction and constrained hand-only arm inference

Update body_tracking.py and both Unity controller scripts together. Check Invert Roll in UnityChanController to reverse the previously mirrored side tilt. Invert Pitch remains unchecked; Invert Yaw remains checked for the requested anatomical left/right convention.

Infer Arm From Hand (enabled by default) handles a detailed hand detection when the arm pose is unreliable. It uses visible image-space shoulders as an anchor and scale, projects the detected wrist into avatar space, and solves a two-bone arm with fixed bone lengths. Reach is limited to 95%, the elbow uses an outward/downward/front bend guide, and the uncertain wrist is kept in front with limited torso crossing. Existing smoothing limits still apply. Because a single hand image cannot recover a hidden elbow's true position or absolute wrist depth, this is a conservative approximation, not guaranteed anatomical reconstruction. If shoulder anchors/scale or bone geometry are missing, the controller holds/returns the arm instead of inventing a position. Disable Infer Arm From Hand to retain the previous fallback. Keep Require Hand For Arm Tracking unchecked so reliable arms remain independent of fingers.

Depth Motion now defaults to Disabled for new components. Existing Inspector values are preserved: explicitly choose Disabled to turn it off. You can opt into Upper Body Lean (fixed hips) or Whole Avatar (root translation) whenever desired. Image-pose anchors are optional added packet data; old Python senders without imagePose cannot use the new arm inference and fall back safely.

The optional Unity Editor regression checks include hand-inferred IK length preservation and reach limits. Python tests check consistent coordinate units for shoulder anchors and hand wrists. Unity visual verification is still required for mirrored tilt and hidden-arm appearance on the actual model.

### Unity-Chan iris / eye movement

The updated UnityChanController.cs now consumes the already-transmitted iris ratios (face fields 5-8) and drives eye bones in LateUpdate after head tracking. Python commands and the legacy 11-float face format are unchanged. Replace the existing UnityChanController.cs in your project; do not add a duplicate class.

Enable Eye Tracking is on by default. Left Eye and Right Eye are auto-filled from Humanoid LeftEye/RightEye mappings. If no eye bones are mapped, assign the actual eyeball rotation transforms manually (not the eyelid mesh renderers). Missing eye bones are skipped with a startup warning; head/blink/mouth tracking can continue. Disable other scripts such as gaze/LookAt controllers that also rotate the same eyeballs.

At first usable face sample, look straight at the camera to calibrate gaze neutral. With the Unity Game window focused, press G to recalibrate while looking forward, or use the component context menu Recalibrate gaze neutral. This is separate from C in the Python preview, which calibrates head/depth. Start with Maximum Eye Yaw = 20 degrees, Maximum Eye Pitch = 12, Eye Smoothing = 12, Iris Horizontal Range = 0.25, Iris Vertical Range = 0.3. Smaller iris ranges increase sensitivity. Toggle Invert Eye Horizontal/Vertical only if needed for your rig/convention; head direction settings do not change gaze direction. Eye offsets use the model's initial eye orientation and avatar axes, rather than assuming every eye bone has the same axes.

Both usable iris measurements are averaged to keep eyes looking together. A blinking eye is excluded using Gaze Blink Threshold (default 0.15); when both blink, gaze holds. If gaze never responds, check eye references and the blink threshold against your measured EAR values. Lost face data eases eyes toward neutral after 0.5 seconds. This remains approximate iris-based gaze, not precise eye tracking. It does not include automatic convergence onto nearby objects.

The face receiver now buffers whitespace-delimited numbers across fragmented/coalesced TCP reads, parses invariant-culture floats, and applies complete 11-number samples atomically on the main thread. Connections can restart without aborting the receiver thread. Stop/Destroy closes sockets instead of using Thread.Abort.

Optional Unity Editor checks: copy UnityAssets/Tests/Editor/EyeTrackingRegressionChecks.cs into Assets/Editor and run Tools > VTuber > Run eye tracking regression checks outside Play. Checks cover gaze limits/direction/neutral, split/coalesced packets, invalid frame recovery and main-thread iris field application. They have not been executed in this cloud environment; eye-bone movement still needs visual testing in Unity. Verify neutral gaze, left/right/up/down, blinking, head turns and Python disconnect/reconnect.

The G gaze-calibration shortcut supports both Unity input backends: it uses Keyboard.current when ENABLE_INPUT_SYSTEM is active, otherwise the legacy Input API only when ENABLE_LEGACY_INPUT_MANAGER is active. No Active Input Handling change is needed. If no keyboard backend is available, use the component context menu Recalibrate gaze neutral.

### Flat eye meshes (eye_L_old / eye_R_old): use material gaze

A dedicated eye mesh is not necessarily an eyeball bone with a usable rotation pivot. For flat imported meshes such as eye_L_old, rotating its transform can pull the eye surface away from its socket. Stop Play to restore runtime transforms; do not zero imported mesh rotations.

The updated UnityChanController has Eye Drive Mode = Automatic by default. If an assigned eye transform has a Renderer, Automatic uses material offsets and never rotates that mesh. True eye-bone transforms without a renderer still use bone rotation. For this model explicitly select Material Offset, assign Left Eye Renderer = eye_L_old's Mesh Renderer and Right Eye Renderer = the corresponding right-eye renderer. The old Left Eye/Right Eye transform fields are used only for bone rotation or automatic renderer discovery. Enable Eye Tracking, keep Eye Material Index = 0 for a single-material eye, and Eye Texture Property = _MainTex for the shown Toon/Toon material. URP/custom shaders may use _BaseMap or another property instead.

Start with Maximum Eye UV Offset = (0.02, 0.015), reducing either component if the iris moves too far. Left/Right Eye UV Direction default to (1,1); set one eye's X or Y to -1 if that mesh's UVs are mirrored. Gaze calibration (G in the Game window), blink filtering and smoothing still apply. Per-renderer MaterialPropertyBlocks change only texture sampling offsets; mesh transforms and shared material assets are untouched, and the original texture offset is restored on disable/destroy. Whole-texture movement is suitable only for a dedicated eye texture; a face atlas or unusual UV layout may require a dedicated iris shader instead.

The shader must actually use its texture scale/offset (_MainTex_ST or the selected property's _ST). HasProperty alone cannot prove that. The screenshot's Toon/Toon shader is not the UnityChan/Eye shader bundled in this repository, so its behavior has not been verified here. If offsets have no visible effect, inspect the shader rather than increasing mesh rotation or UV offset. This approach moves the existing texture; it does not separate the iris from baked-in highlights/sclera artwork.

Optional Editor eye checks now cover bounded UV offsets, no mesh rotation in automatic material mode, no shared-material mutation and offset restoration (the material checks require Unity's Unlit/Texture shader). These checks are supplied but unrun here; actual Toon/Toon rendering still needs testing in Unity.

### Gaze sensitivity and a renderer-only preview

Replace UnityChanController.cs with the latest version. Gaze Sensitivity is a per-axis multiplier on iris displacement BEFORE limiting, with X horizontal and Y vertical; start at (3,3) for a more obvious response. Maximum Eye UV Offset controls travel in Material Offset mode independently of that sensitivity. Start a temporary visibility test at (0.08,0.06), then reduce it to suit the eye texture. UV travel is now bounded at 0.25 per axis rather than 0.1. Large offsets can reveal texture seams/wrapping and are not recommended as a permanent setting.

To distinguish shader setup from camera/threshold problems, enter Play in Material Offset mode and check Preview Eye Movement. It sweeps gaze horizontally and vertically without Python, iris data, calibration or blink thresholds, keeping the eyelids open for visibility. It uses the configured renderer and texture property and never rotates a mesh in material mode. Turn preview OFF after testing. If this exaggerated sweep is invisible, increasing sensitivity cannot fix it: check the assigned renderer/material slot and whether the shader uses its texture _ST offset. If preview works but live gaze does not, check Gaze Blink Threshold, G calibration and live iris input.

Removed unused imports, obsolete hidden eyelid parameters, commented-out startup/debug code, and per-frame head-angle Console spam from UnityChanController. Existing serialized settings, save/load methods, alternate bone-eye support, input-backend support and the active face protocol are preserved. The optional Editor eye regression checks include sensitivity-before-clamping, wider UV limits and renderer preview without camera input; they remain unrun without Unity Editor.
