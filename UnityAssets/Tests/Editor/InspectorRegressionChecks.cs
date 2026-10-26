#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VTuber.EditorTools;

public static class InspectorRegressionChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("Tools/VTuber/Run Inspector settings regression checks")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        GameObject root = new GameObject("Temporary Inspector settings check");
        try
        {
            var body = root.AddComponent<HumanoidBodyController>();
            body.maximumAngularSpeed = 333;
            body.maximumWristAngularSpeed = 111;
            body.enableOptionalEstimates = true;
            body.inferArmFromHand = true;
            body.allowShoulderOnlyTorso = true;
            body.depthMotion = HumanoidBodyController.DepthMotionMode.WholeAvatar;
            body.drawTrackingGizmos = true;
            var data = new SerializedObject(body);
            data.Update();
            foreach (string field in new[] { "maximumAngularSpeed", "maximumWristAngularSpeed",
                "maximumFingerAngularSpeed", "maximumTorsoAngularSpeed", "wristSmoothing",
                "fingerSmoothing", "torsoSmoothing", "minimumWristBend", "maximumWristBend" })
                Require(data.FindProperty(field) != null, "Missing serialized part setting: " + field);
            TrackingInspector.ResetOptional(data);
            data.ApplyModifiedProperties();
            Require(!body.enableOptionalEstimates && !body.inferArmFromHand && !body.allowShoulderOnlyTorso,
                "Optional estimates were not turned off.");
            Require(body.depthMotion == HumanoidBodyController.DepthMotionMode.Disabled && !body.drawTrackingGizmos,
                "Optional depth/diagnostics were not turned off.");
            Require(body.maximumAngularSpeed == 333 && body.maximumWristAngularSpeed == 111,
                "Optional reset changed core motion tuning.");

            var face = root.AddComponent<UnityChanController>();
            face.max_rotation_angle = 55;
            face.gazeSensitivity = new Vector2(3, 5);
            face.previewEyeMovement = face.isAutoBlinkActive = true;
            data = new SerializedObject(face);
            data.Update();
            Require(data.FindProperty("port") != null, "Face port is not serialized for the Inspector.");
            TrackingInspector.ResetOptional(data);
            data.ApplyModifiedProperties();
            Require(!face.previewEyeMovement && !face.isAutoBlinkActive, "Face optional controls were not reset.");
            Require(face.max_rotation_angle == 55 && face.gazeSensitivity == new Vector2(3, 5),
                "Optional reset changed head/gaze tuning.");
            Debug.Log("PASS: grouped settings exist, optional reset preserves core tuning.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
