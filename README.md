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
