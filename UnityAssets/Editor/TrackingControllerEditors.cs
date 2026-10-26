#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace VTuber.EditorTools
{
// Editor-only presentation. Runtime field names stay unchanged for prefab/scene
// compatibility; foldout state is stored in the editor session, not the model.
static class TrackingInspector
{
    public static void Group(string key, string title, Action draw, bool openByDefault = true)
    {
        string id = "VTuber.Inspector." + key;
        bool open = EditorGUILayout.Foldout(SessionState.GetBool(id, openByDefault), title, true);
        SessionState.SetBool(id, open);
        if (!open) return;
        EditorGUI.indentLevel++;
        try { draw(); }
        finally { EditorGUI.indentLevel--; }
        EditorGUILayout.Space(3);
    }

    public static void Field(SerializedObject data, string name, string label, string tooltip = null)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property == null) { EditorGUILayout.HelpBox("Missing setting: " + name, MessageType.Warning); return; }
        EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
    }

    public static void Response(SerializedObject data, string key, string rate, string speed, string deadZone)
    {
        Group(key, "Response", () => {
            Field(data, rate, "Blend Rate (1/s)", "0 disables the low-pass stage; otherwise higher follows faster. Speed limits still apply. Used for rest transitions and legacy input; adaptive-filtered tracking avoids a second low-pass stage.");
            Field(data, speed, "Maximum Speed (deg/s)", "Rotation rate limit, independent of the position/angle limit.");
            Field(data, deadZone, "Dead Zone (deg)", "Ignore tiny target changes. Large values suppress intentional small movement.");
        });
    }

    public static void ResetOptional(SerializedObject data)
    {
        foreach (string name in new[] { "enableOptionalEstimates", "inferArmFromHand", "allowShoulderOnlyTorso", "drawTrackingGizmos", "previewEyeMovement", "isAutoBlinkActive" })
        {
            SerializedProperty property = data.FindProperty(name);
            if (property != null) property.boolValue = false;
        }
        SerializedProperty depth = data.FindProperty("depthMotion");
        if (depth != null) depth.enumValueIndex = 0;
    }
}

[CustomEditor(typeof(HumanoidBodyController)), CanEditMultipleObjects]
public class HumanoidBodyControllerEditor : Editor
{
    void F(string name, string label, string tooltip = null) { TrackingInspector.Field(serializedObject, name, label, tooltip); }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        TrackingInspector.Group("body.connection", "Connection & Confidence", () => {
            F("port", "Body TCP Port", "Restart Play after changing the port.");
            F("minimumVisibility", "Minimum Visibility (0–1)");
            F("trackingTimeout", "Packet Timeout (s)", "No packets: release tracked torso control and use the selected resting policy.");
            F("invertDepth", "Invert Front / Back", "Axis mapping only; does not enable estimated depth movement.");
        }, false);
        TrackingInspector.Group("body.arms", "Arms", () => {
            TrackingInspector.Response(serializedObject, "body.arms.response", "smoothing", "maximumAngularSpeed", "rotationDeadZone");
            TrackingInspector.Group("body.arms.limits", "Limits", () => {
                F("minimumElbowBend", "Minimum Elbow Bend (deg)"); F("maximumElbowBend", "Maximum Elbow Bend (deg)");
                F("limitElbowInward", "Limit Inward Elbow Movement");
                if (serializedObject.FindProperty("limitElbowInward").boolValue)
                    F("elbowSideClearance", "Elbow Clearance (shoulder widths)", "Distance from the torso midline. Smaller permits more cross-body motion; this is not a crossing-pose guess.");
            });
        });
        TrackingInspector.Group("body.wrists", "Wrists", () => {
            TrackingInspector.Response(serializedObject, "body.wrists.response", "wristSmoothing", "maximumWristAngularSpeed", "wristRotationDeadZone");
            TrackingInspector.Group("body.wrists.limits", "Limits", () => {
                F("limitWristBend", "Enable Wrist Limits");
                if (serializedObject.FindProperty("limitWristBend").boolValue) {
                    F("maximumWristRotation", "Maximum Total Rotation (deg)");
                    F("minimumWristBend", "Minimum Bend / Extension (deg)");
                    F("maximumWristBend", "Maximum Bend / Flexion (deg)");
                }
            });
        });
        TrackingInspector.Group("body.fingers", "Fingers", () => {
            TrackingInspector.Response(serializedObject, "body.fingers.response", "fingerSmoothing", "maximumFingerAngularSpeed", "fingerRotationDeadZone");
            TrackingInspector.Group("body.fingers.limits", "Limits", () => {
                F("maximumFingerJointRotation", "Maximum Joint Rotation (deg)", "Bind-relative safety cone; not a calibrated one-axis hinge limit.");
            });
        });
        TrackingInspector.Group("body.torso", "Torso", () => {
            TrackingInspector.Response(serializedObject, "body.torso.response", "torsoSmoothing", "maximumTorsoAngularSpeed", "torsoRotationDeadZone");
            TrackingInspector.Group("body.torso.limits", "Limits", () => F("maximumTorsoAngle", "Maximum Lean / Turn (deg)"));
        }, false);
        TrackingInspector.Group("body.loss", "Tracking Loss & Rest Pose (shared)", () => {
            F("occlusionGracePeriod", "Hold Last Pose (s)", "Applies to arms, wrists and fingers while packets continue but landmarks are missing.");
            F("returnToRestWhenLost", "Return to Rest After Hold");
            F("useRelaxedArmRestPose", "Use Relaxed Arm Rest Pose");
            if (serializedObject.FindProperty("useRelaxedArmRestPose").boolValue) {
                F("restingArmSideAngle", "Rest Arm Side Angle (deg)");
                F("restingElbowBend", "Rest Elbow Bend (deg)");
            }
        }, false);
        TrackingInspector.Group("body.optional", "Optional Estimates — OFF by default", () => {
            EditorGUILayout.HelpBox("Estimates run only when the master switch is enabled. Saved values are retained; reset clears optional choices without changing core tracking/safety tuning.", MessageType.Info);
            F("enableOptionalEstimates", "Enable Optional Estimates");
            bool enabled = serializedObject.FindProperty("enableOptionalEstimates").boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                F("inferArmFromHand", "Estimate Missing Arm from Hand");
                F("allowShoulderOnlyTorso", "Estimate Torso from Shoulders Only");
            }
            TrackingInspector.Group("body.optional.depth", "Estimated Depth Movement", () => {
                using (new EditorGUI.DisabledScope(!enabled)) F("depthMotion", "Depth Movement Mode");
                int mode = serializedObject.FindProperty("depthMotion").enumValueIndex;
                using (new EditorGUI.DisabledScope(!enabled || mode == 0)) {
                    if (mode != 2) F("maximumDepthLean", "Maximum Added Lean (deg)");
                    if (mode != 1) {
                        F("depthTranslationScale", "Translation Gain");
                        F("maximumDepthTranslation", "Maximum Translation (local units)");
                    }
                    F("depthSmoothing", "Blend Rate (1/s)");
                }
            }, false);
            if (GUILayout.Button("Use Conservative Optional Defaults")) TrackingInspector.ResetOptional(serializedObject);
        }, false);
        TrackingInspector.Group("body.diagnostics", "Diagnostics", () => {
            F("drawTrackingGizmos", "Draw IK Targets in Scene View");
            using (new EditorGUI.DisabledScope(true)) {
                F("torsoTracking", "Torso Tracking Active"); F("leftArmInferred", "Left Arm Estimated"); F("rightArmInferred", "Right Arm Estimated");
            }
        }, false);
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(UnityChanController)), CanEditMultipleObjects]
public class UnityChanControllerEditor : Editor
{
    void F(string name, string label, string tooltip = null) { TrackingInspector.Field(serializedObject, name, label, tooltip); }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        TrackingInspector.Group("face.connection", "Connection", () => F("port", "Face TCP Port", "Restart Play after changing the port."), false);
        TrackingInspector.Group("face.references", "Model References", () => {
            F("ref_SMR_EYE_DEF", "Eye Blend-shape Renderer"); F("ref_SMR_EL_DEF", "Eyelid Blend-shape Renderer");
            F("ref_SMR_MTH_DEF", "Mouth Blend-shape Renderer");
        }, false);
        TrackingInspector.Group("face.head", "Head", () => {
            TrackingInspector.Group("face.head.response", "Response", () => F("headSmoothing", "Blend Rate (1/s)", "0 disables low-pass blending; otherwise higher follows faster."));
            TrackingInspector.Group("face.head.limits", "Limits", () => F("max_rotation_angle", "Maximum Rotation per Axis (deg)"));
            TrackingInspector.Group("face.head.mapping", "Axis Mapping & Torso Compensation", () => {
                F("invertPitch", "Invert Pitch (nod)"); F("invertYaw", "Invert Yaw (turn)"); F("invertRoll", "Invert Roll (tilt)");
                F("compensateTorsoMotion", "Compensate Torso Rotation");
                EditorGUILayout.HelpBox("C in the Python preview recalibrates head neutral. G in Unity recalibrates gaze.", MessageType.Info);
            }, false);
        });
        TrackingInspector.Group("face.eyes", "Eyes / Gaze", () => {
            F("enableEyeTracking", "Enable Gaze Tracking");
            if (!serializedObject.FindProperty("enableEyeTracking").boolValue) return;
            int mode = serializedObject.FindProperty("eyeDriveMode").enumValueIndex;
            TrackingInspector.Group("face.eyes.references", "Drive Method & References", () => {
                F("eyeDriveMode", "Eye Drive Method");
                if (mode != 1) {
                    F("leftEyeRenderer", "Left Eye Surface Renderer"); F("rightEyeRenderer", "Right Eye Surface Renderer");
                    F("eyeMaterialIndex", "Material Slot (starts at 0)"); F("eyeTextureProperty", "Texture Shader Property");
                }
                if (mode != 2) TrackingInspector.Group("face.eyes.bones", "Eye Bones (not flat meshes)", () => {
                    F("leftEye", "Left Eyeball Bone"); F("rightEye", "Right Eyeball Bone");
                }, mode == 1);
            }, false);
            TrackingInspector.Group("face.eyes.response", "Response", () => {
                F("gazeSensitivity", "Sensitivity (X horizontal, Y vertical)");
                F("eyeSmoothing", "Blend Rate (1/s)", "0 disables low-pass blending; otherwise higher follows faster.");
                F("irisHorizontalRange", "Horizontal Input Range (iris ratio)"); F("irisVerticalRange", "Vertical Input Range (iris ratio)");
                F("invertEyeHorizontal", "Invert Horizontal"); F("invertEyeVertical", "Invert Vertical");
            });
            TrackingInspector.Group("face.eyes.limits", "Limits", () => {
                if (mode != 1) {
                    F("maximumEyeUvOffset", "Maximum Texture Travel (UV)", "UV travel, not degrees. Runtime safety cap is 0.25 per axis.");
                    F("leftEyeUvDirection", "Left UV Direction (X/Y sign)"); F("rightEyeUvDirection", "Right UV Direction (X/Y sign)");
                }
                if (mode != 2) { F("maximumEyeYaw", "Maximum Bone Yaw (deg)"); F("maximumEyePitch", "Maximum Bone Pitch (deg)"); }
            });
            TrackingInspector.Group("face.eyes.neutral", "Neutral Calibration", () => {
                F("calibrateEyesOnFirstSample", "Calibrate on First Open-eye Sample"); F("gazeNeutral", "Neutral Iris Ratio (X/Y)");
                if (Application.isPlaying && GUILayout.Button("Recalibrate Gaze Neutral"))
                    foreach (UnityEngine.Object item in targets) ((UnityChanController)item).RecalibrateEyes();
            }, false);
        });
        TrackingInspector.Group("face.blink", "Blinking / Eye Confidence", () => {
            F("ear_min_threshold", "Closed-eye Threshold (EAR)"); F("ear_max_threshold", "Open-eye Threshold (EAR)");
            F("gazeBlinkThreshold", "Minimum Gaze Opening (EAR)");
            if (serializedObject.FindProperty("ear_min_threshold").floatValue >= serializedObject.FindProperty("ear_max_threshold").floatValue)
                EditorGUILayout.HelpBox("Closed-eye threshold must be lower than open-eye threshold.", MessageType.Error);
        }, false);
        TrackingInspector.Group("face.mouth", "Mouth", () => {
            F("mar_min_threshold", "Closed-mouth Threshold (MAR)"); F("mar_max_threshold", "Open-mouth Threshold (MAR)");
            if (serializedObject.FindProperty("mar_min_threshold").floatValue >= serializedObject.FindProperty("mar_max_threshold").floatValue)
                EditorGUILayout.HelpBox("Closed-mouth threshold must be lower than open-mouth threshold.", MessageType.Error);
        }, false);
        TrackingInspector.Group("face.optional", "Optional / Diagnostics — OFF by default", () => {
            F("isAutoBlinkActive", "Use Automatic Blinking Instead of Camera");
            F("previewEyeMovement", "Preview Eye Movement (no camera)");
            if (GUILayout.Button("Use Conservative Optional Defaults")) TrackingInspector.ResetOptional(serializedObject);
        }, false);
        serializedObject.ApplyModifiedProperties();
    }
}
}
#endif
