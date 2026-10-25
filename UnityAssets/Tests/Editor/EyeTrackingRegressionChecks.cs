#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class EyeTrackingRegressionChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("Tools/VTuber/Run eye tracking regression checks")]
    public static void Run()
    {
        Vector2 neutral = new Vector2(0.5f, 0.5f);
        Require(UnityChanController.GazeAngles(neutral, neutral, 0.25f, 0.3f, 20, 12, false, false)
            == Vector2.zero, "Neutral iris moved the eyes.");
        Vector2 limit = UnityChanController.GazeAngles(Vector2.one, neutral, 0.25f, 0.3f, 20, 12, false, false);
        Require(limit == new Vector2(12, 20), "Eye angles exceeded configured limits.");
        Require(UnityChanController.GazeAngles(Vector2.one, neutral, 0.25f, 0.3f, 20, 12, true, true)
            == -limit, "Gaze direction toggles failed.");

        var decoder = new UnityChanController.FaceStreamDecoder();
        var samples = new List<float[]>();
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            // Fragment every token, then coalesce complete samples in one read.
            string frame = "1 2 3 0.4 0.5 0.6 0.7 0.8 0.9 1 2 ";
            foreach (char c in frame) decoder.Feed(c.ToString(), samples.Add);
            decoder.Feed(frame + frame, samples.Add);
            Require(samples.Count == 3, "Split/coalesced TCP sample count is wrong.");
            Require(Mathf.Abs(samples[0][5] - 0.6f) < 0.001f
                && Mathf.Abs(samples[0][8] - 0.9f) < 0.001f, "Iris fields were decoded incorrectly.");
            decoder.Feed("1 2 3 4 5 NaN 7 8 9 10 11 " + frame, samples.Add);
            Require(samples.Count == 4, "Invalid frame was published or recovery lost alignment.");
            decoder.Feed("123.", samples.Add);
            Require(samples.Count == 4, "Partial token was published.");
        }
        finally { CultureInfo.CurrentCulture = previous; }

        if (Application.isPlaying) throw new InvalidOperationException("Run sample application checks outside Play mode.");
        var root = new GameObject("Temporary eye tracking check");
        try
        {
            var controller = root.AddComponent<UnityChanController>();
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(UnityChanController);
            type.GetField("pendingFace", hidden).SetValue(controller, samples[0]);
            type.GetMethod("ConsumeFace", hidden).Invoke(controller, null);
            Require(Mathf.Abs((float)type.GetField("x_ratio_left", hidden).GetValue(controller) - 0.6f) < 0.001f,
                "Left iris was not applied on main thread.");
            Require(Mathf.Abs((float)type.GetField("y_ratio_right", hidden).GetValue(controller) - 0.9f) < 0.001f,
                "Right iris was not applied on main thread.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("PASS: gaze neutral/limits/directions, fragmented face transport, invariant parsing, iris sample application.");
    }
}
#endif
