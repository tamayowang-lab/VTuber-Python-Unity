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

        Vector2 subtle = UnityChanController.GazeAngles(new Vector2(0.52f, 0.52f), neutral,
            0.25f, 0.3f, 20, 12, false, false);
        Vector2 amplified = UnityChanController.GazeAngles(new Vector2(0.52f, 0.52f), neutral,
            0.25f, 0.3f, 20, 12, false, false, 3, 3);
        Require((amplified - subtle * 3).magnitude < 0.001f, "Sensitivity did not multiply small iris movements.");
        Vector2 amplifiedLimit = UnityChanController.GazeAngles(Vector2.one, neutral,
            0.25f, 0.3f, 20, 12, false, false, 20, 20);
        Require(amplifiedLimit == limit, "Sensitivity bypassed gaze limits.");
        Vector2 wideUv = UnityChanController.GazeUvOffset(new Vector2(12, 20), 20, 12,
            new Vector2(0.08f, 0.06f), Vector2.one);
        Require(Mathf.Abs(wideUv.x + 0.08f) < 0.0001f && Mathf.Abs(wideUv.y - 0.06f) < 0.0001f,
            "Wide UV diagnostic range was clipped to the previous limit.");

        Vector2 uv = UnityChanController.GazeUvOffset(new Vector2(12, 20), 20, 12,
            new Vector2(0.02f, 0.015f), Vector2.one);
        Require(Mathf.Abs(uv.x + 0.02f) < 0.0001f && Mathf.Abs(uv.y - 0.015f) < 0.0001f,
            "UV movement did not respect configured direction/limits.");
        Require(UnityChanController.GazeUvOffset(Vector2.zero, 20, 12, Vector2.one, Vector2.one) == Vector2.zero,
            "Neutral gaze changed the texture offset.");

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
            GameObject surfaceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surfaceObject.transform.SetParent(root.transform);
            surfaceObject.transform.localRotation = Quaternion.Euler(10, 20, 30);
            Quaternion beforeRotation = surfaceObject.transform.localRotation;
            Renderer renderer = surfaceObject.GetComponent<Renderer>();
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader != null)
            {
                Material material = new Material(shader);
                try
                {
                    material.SetTextureOffset("_MainTex", new Vector2(0.1f, 0.2f));
                    renderer.sharedMaterial = material;
                    object[] configure = { surfaceObject.transform, renderer, false, null };
                    type.GetMethod("ConfigureEye", hidden).Invoke(controller, configure);
                    Require(!(bool)configure[2], "Automatic mode tried to rotate a mesh surface.");
                    Require(configure[3] != null, "Supported material surface was not configured.");
                    type.GetMethod("ApplyEyeSurface", hidden).Invoke(controller, new object[] { configure[3], uv });
                    Require(Quaternion.Angle(surfaceObject.transform.localRotation, beforeRotation) < 0.001f,
                        "Material gaze rotated the mesh.");
                    Require(material.GetTextureOffset("_MainTex") == new Vector2(0.1f, 0.2f),
                        "Material gaze mutated the shared material.");
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, 0);
                    Vector4 st = block.GetVector(Shader.PropertyToID("_MainTex_ST"));
                    Require(Mathf.Abs(st.z - (0.1f + uv.x)) < 0.0001f,
                        "Per-renderer UV offset was not applied.");
                    type.GetField("leftSurface", hidden).SetValue(controller, configure[3]);
                    type.GetMethod("RestoreEyeSurfaces", hidden).Invoke(controller, null);
                    renderer.GetPropertyBlock(block, 0);
                    Require(Mathf.Abs(block.GetVector(Shader.PropertyToID("_MainTex_ST")).z - 0.1f) < 0.0001f,
                        "Material gaze did not restore its initial offset.");
                    // Preview must bypass missing face packets and the blink gate.
                    controller.previewEyeMovement = true;
                    controller.eyeSmoothing = 0;
                    type.GetField("hasFaceData", hidden).SetValue(controller, false);
                    type.GetMethod("EyeMovement", hidden).Invoke(controller, null);
                    Vector2 previewAngles = (Vector2)type.GetField("filteredGazeAngles", hidden).GetValue(controller);
                    Vector2 expectedPreview = new Vector2(Mathf.Cos(Time.unscaledTime * 0.9f) * controller.maximumEyePitch,
                        Mathf.Sin(Time.unscaledTime * 1.2f) * controller.maximumEyeYaw);
                    Require((previewAngles - expectedPreview).magnitude < 0.001f,
                        "Diagnostic sweep incorrectly required live face or open-eye input.");
                    Require(Quaternion.Angle(surfaceObject.transform.localRotation, beforeRotation) < 0.001f,
                        "Diagnostic material sweep rotated the mesh.");
                    controller.previewEyeMovement = false;
                    type.GetMethod("EyeMovement", hidden).Invoke(controller, null);
                    Require((Vector2)type.GetField("filteredGazeAngles", hidden).GetValue(controller) == Vector2.zero,
                        "Leaving preview with no input did not restore neutral gaze.");
                }
                finally { UnityEngine.Object.DestroyImmediate(material); }
            }
            else Debug.LogWarning("Unlit/Texture unavailable: material-property-block checks were skipped.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("PASS: gaze neutral/limits/directions, fragmented face transport, invariant parsing, iris sample application.");
    }
}
#endif
