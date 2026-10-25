using UnityEngine;

using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Globalization;

[DefaultExecutionOrder(200)]
public class UnityChanController : MonoBehaviour, ISaveable
{
    public SkinnedMeshRenderer ref_SMR_EYE_DEF;	//EYE_DEFへの参照
    public SkinnedMeshRenderer ref_SMR_EL_DEF;	//EL_DEFへの参照

    public float max_rotation_angle = 45.0f;
    public bool invertPitch = false;
    public bool invertYaw = true;
    public bool invertRoll = true;
    [Min(0)] public float headSmoothing = 10f;
    public bool compensateTorsoMotion = true;

    [Header("Iris / gaze tracking")]
    public bool enableEyeTracking = true;
    public enum EyeDriveMode { Automatic, BoneRotation, MaterialOffset }
    public EyeDriveMode eyeDriveMode = EyeDriveMode.Automatic;
    public Renderer leftEyeRenderer;
    public Renderer rightEyeRenderer;
    [Min(0)] public int eyeMaterialIndex;
    public string eyeTextureProperty = "_MainTex";
    public Vector2 maximumEyeUvOffset = new Vector2(0.02f, 0.015f);
    public Vector2 leftEyeUvDirection = Vector2.one;
    public Vector2 rightEyeUvDirection = Vector2.one;
    public Transform leftEye;
    public Transform rightEye;
    [Range(0, 35)] public float maximumEyeYaw = 20f;
    [Range(0, 25)] public float maximumEyePitch = 12f;
    [Min(0)] public float eyeSmoothing = 12f;
    [Tooltip("Multiply iris displacement before the gaze limit. X = horizontal, Y = vertical.")]
    public Vector2 gazeSensitivity = Vector2.one;
    [Tooltip("Sweep the eyes without Python/blink input to test the renderer or shader. Turn off after testing.")]
    public bool previewEyeMovement;
    [Min(0.01f)] public float irisHorizontalRange = 0.25f;
    [Min(0.01f)] public float irisVerticalRange = 0.3f;
    public bool invertEyeHorizontal;
    public bool invertEyeVertical;
    [Min(0)] public float gazeBlinkThreshold = 0.15f;
    public Vector2 gazeNeutral = new Vector2(0.5f, 0.5f);
    public bool calibrateEyesOnFirstSample = true;

    public float ear_max_threshold = 0.38f;
    public float ear_min_threshold = 0.30f;

    public bool isAutoBlinkActive;
    private MonoBehaviour autoBlinkScript;

    public SkinnedMeshRenderer ref_SMR_MTH_DEF;     // the mouth component

    public float mar_max_threshold = 1.0f;
    public float mar_min_threshold = 0.0f;

    private Transform neck;
    private Quaternion neckRestLocalRotation;
    private Quaternion avatarToNeckParent;
    private Quaternion neckRestAvatarRotation;
    private volatile bool hasFaceData;
    private readonly object faceGate = new object();
    private float[] pendingFace;
    private volatile bool stopping;
    private float lastFaceTime = float.NegativeInfinity;
    private bool gazeCalibrated, gazeCalibrationRequested;
    private Quaternion leftEyeRest, rightEyeRest, leftEyeFrame, rightEyeFrame;
    private Vector2 filteredGazeAngles;
    private bool rotateLeftEye, rotateRightEye, gazeWasDriven;
    class EyeSurface
    {
        public Renderer renderer;
        public int slot, property;
        public Vector4 initialST;
        public MaterialPropertyBlock block = new MaterialPropertyBlock();
    }
    private EyeSurface leftSurface, rightSurface;
    private Quaternion filteredHeadOffset = Quaternion.identity;

        Thread receiveThread;
    TcpClient client;
    TcpListener listener;
    int port = 5066;

    private float roll = 0, pitch = 0, yaw = 0;
    private float x_ratio_left = 0.5f, y_ratio_left = 0.5f, x_ratio_right = 0.5f, y_ratio_right = 0.5f;
    private float ear_left = 0, ear_right = 0;
    private float mar = 0;

    void Start()
    {
        Animator anim = GetComponent<Animator>();

        neck = anim.GetBoneTransform(HumanBodyBones.Neck);
        // Preserve this avatar's neutral neck instead of assuming Unity-Chan bone axes.
        neckRestLocalRotation = neck.localRotation;
        neckRestAvatarRotation = Quaternion.Inverse(transform.rotation) * neck.rotation;
        avatarToNeckParent = Quaternion.Inverse(neck.parent.rotation) * transform.rotation;
        autoBlinkScript = GetComponent("AutoBlink") as MonoBehaviour;
        if (autoBlinkScript != null) autoBlinkScript.enabled = isAutoBlinkActive;
        SetEyes(0);
        if (leftEye == null) leftEye = anim.GetBoneTransform(HumanBodyBones.LeftEye);
        if (rightEye == null) rightEye = anim.GetBoneTransform(HumanBodyBones.RightEye);
        ConfigureEye(leftEye, leftEyeRenderer, out rotateLeftEye, out leftSurface);
        ConfigureEye(rightEye, rightEyeRenderer, out rotateRightEye, out rightSurface);
        CaptureEye(rotateLeftEye ? leftEye : null, out leftEyeRest, out leftEyeFrame);
        CaptureEye(rotateRightEye ? rightEye : null, out rightEyeRest, out rightEyeFrame);
        if (enableEyeTracking && !rotateLeftEye && !rotateRightEye && leftSurface == null && rightSurface == null)
            Debug.LogWarning("Eye tracking needs eye bones or dedicated eye mesh renderers with a supported texture property.", this);

        InitTCP();
    }

    // Launch TCP to receive message from python
    private void InitTCP() {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("Face tracking listener: " + e.Message, this);
            return;
        }
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    // The legacy sender emits 11 whitespace-separated floats per face sample.
    // TCP reads can split a number or contain several samples. Decode complete
    // tokens before publishing an atomic sample to Unity's main thread.
    public class FaceStreamDecoder
    {
        readonly StringBuilder token = new StringBuilder();
        readonly float[] values = new float[11];
        int count;
        bool invalid;
        public void Feed(string chunk, Action<float[]> publish)
        {
            foreach (char c in chunk)
            {
                if (!char.IsWhiteSpace(c))
                {
                    if (token.Length >= 64) throw new IOException("Face token exceeds 64 characters.");
                    token.Append(c);
                    continue;
                }
                if (token.Length == 0) continue;
                float value;
                bool valid = float.TryParse(token.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
                invalid |= !valid;
                values[count++] = valid ? value : 0;
                token.Length = 0;
                if (count == values.Length)
                {
                    if (!invalid) publish((float[])values.Clone());
                    count = 0;
                    invalid = false;
                }
            }
        }
    }

    private void ReceiveData()
    {
        try
        {
            byte[] bytes = new byte[4096];
            while (!stopping)
            {
                TcpClient accepted = listener.AcceptTcpClient();
                lock (faceGate)
                {
                    if (stopping) { accepted.Close(); return; }
                    client = accepted;
                }
                try
                {
                    using (accepted)
                    using (NetworkStream stream = accepted.GetStream())
                    {
                        FaceStreamDecoder decoder = new FaceStreamDecoder();
                        int length;
                        while (!stopping && (length = stream.Read(bytes, 0, bytes.Length)) > 0)
                            decoder.Feed(Encoding.ASCII.GetString(bytes, 0, length), sample => {
                                lock (faceGate) pendingFace = sample;
                            });
                    }
                }
                catch (IOException) { /* Allow a sender to reconnect. */ }
                catch (SocketException) { }
                finally { lock (faceGate) client = null; }
            }
        }
        catch (Exception e) { if (!stopping) Console.Error.WriteLine("Face tracking receiver: " + e.Message); }
    }

    void ConsumeFace()
    {
        float[] sample;
        lock (faceGate) { sample = pendingFace; pendingFace = null; }
        if (sample == null) return;
        roll = sample[0]; pitch = sample[1]; yaw = sample[2];
        ear_left = sample[3]; ear_right = sample[4];
        x_ratio_left = sample[5]; y_ratio_left = sample[6];
        x_ratio_right = sample[7]; y_ratio_right = sample[8];
        mar = sample[9];
        hasFaceData = true;
        lastFaceTime = Time.time;
    }

    static bool GazeCalibrationPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.GetKeyDown(KeyCode.G);
#else
        // Calibration remains available through the component context menu.
        return false;
#endif
    }

    void Update()
    {
        ConsumeFace();
        if (GazeCalibrationPressed()) RecalibrateEyes();
        // disable the eye blinking control by the user if AutoBlink script is running
        if (!isAutoBlinkActive)
            EyeBlinking();

        MouthMoving();
    }

    // Apply after Animator evaluation. Local rotation also follows the tracked torso.
    void LateUpdate()
    {
        HeadRotation();
        EyeMovement();
    }

    void HeadRotation()
    {
        if (!hasFaceData) return;
        // clamp the angles to prevent unnatural movement
        float pitch_clamp = Mathf.Clamp(invertPitch ? -pitch : pitch, -max_rotation_angle, max_rotation_angle);
        float yaw_clamp = Mathf.Clamp(invertYaw ? -yaw : yaw, -max_rotation_angle, max_rotation_angle);
        float roll_clamp = Mathf.Clamp(invertRoll ? -roll : roll, -max_rotation_angle, max_rotation_angle);

        // do rotation at neck to control the movement of head
        Quaternion target = Quaternion.Euler(pitch_clamp, yaw_clamp, roll_clamp);
        float blend = headSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-headSmoothing * Time.deltaTime);
        filteredHeadOffset = Quaternion.Slerp(filteredHeadOffset, target, blend);
        Quaternion offset = filteredHeadOffset;
        if (compensateTorsoMotion)
            // Face angles are camera-relative, not relative to the moving chest.
            // Run after body retargeting so torso lean is not counted twice.
            neck.rotation = transform.rotation * offset * neckRestAvatarRotation;
        else
            neck.localRotation = avatarToNeckParent * offset
                * Quaternion.Inverse(avatarToNeckParent) * neckRestLocalRotation;
    }

    void ConfigureEye(Transform eye, Renderer surface, out bool rotate, out EyeSurface materialTarget)
    {
        if (surface == null && eye != null) surface = eye.GetComponent<Renderer>();
        // A MeshRenderer assignment is a surface, not proof of an eyeball pivot.
        // Automatic mode never rotates that mesh around its imported origin.
        rotate = eye != null && (eyeDriveMode == EyeDriveMode.BoneRotation
            || (eyeDriveMode == EyeDriveMode.Automatic && surface == null));
        materialTarget = null;
        if (rotate || eyeDriveMode == EyeDriveMode.BoneRotation || surface == null) return;
        Material[] materials = surface.sharedMaterials;
        if (eyeMaterialIndex < 0 || eyeMaterialIndex >= materials.Length || materials[eyeMaterialIndex] == null) return;
        Material material = materials[eyeMaterialIndex];
        string texture = eyeTextureProperty;
        if (string.IsNullOrEmpty(texture)) texture = "_MainTex";
        if (!material.HasProperty(texture))
        {
            Debug.LogWarning("Eye material has no " + texture + " texture. Set Eye Texture Property for its shader.", surface);
            return;
        }
        Vector2 scale = material.GetTextureScale(texture);
        Vector2 offset = material.GetTextureOffset(texture);
        materialTarget = new EyeSurface { renderer = surface, slot = eyeMaterialIndex,
            property = Shader.PropertyToID(texture + "_ST"), initialST = new Vector4(scale.x, scale.y, offset.x, offset.y) };
        surface.GetPropertyBlock(materialTarget.block, eyeMaterialIndex);
        Vector4 previous = materialTarget.block.GetVector(materialTarget.property);
        if (previous.x != 0 || previous.y != 0) materialTarget.initialST = previous;
    }

    public static Vector2 GazeUvOffset(Vector2 angles, float maxYaw, float maxPitch, Vector2 limit, Vector2 direction)
    {
        float horizontal = maxYaw > 0 ? Mathf.Clamp(angles.y / maxYaw, -1, 1) : 0;
        float vertical = maxPitch > 0 ? Mathf.Clamp(angles.x / maxPitch, -1, 1) : 0;
        // Texture sampling offsets move visible texture content in the opposite direction.
        return new Vector2(-horizontal * Mathf.Clamp(Mathf.Abs(limit.x), 0, 0.25f) * Mathf.Clamp(direction.x, -1, 1),
            vertical * Mathf.Clamp(Mathf.Abs(limit.y), 0, 0.25f) * Mathf.Clamp(direction.y, -1, 1));
    }

    void ApplyEyeSurface(EyeSurface surface, Vector2 offset)
    {
        if (surface == null || surface.renderer == null) return;
        surface.renderer.GetPropertyBlock(surface.block, surface.slot);
        Vector4 st = surface.initialST;
        st.z += offset.x;
        st.w += offset.y;
        surface.block.SetVector(surface.property, st);
        surface.renderer.SetPropertyBlock(surface.block, surface.slot);
    }

    void RestoreEyeSurfaces()
    {
        ApplyEyeSurface(leftSurface, Vector2.zero);
        ApplyEyeSurface(rightSurface, Vector2.zero);
    }

    void CaptureEye(Transform eye, out Quaternion rest, out Quaternion frame)
    {
        rest = eye == null ? Quaternion.identity : eye.localRotation;
        frame = eye == null || eye.parent == null ? Quaternion.identity
            : Quaternion.Inverse(eye.parent.rotation) * transform.rotation;
    }

    [ContextMenu("Recalibrate gaze neutral")]
    public void RecalibrateEyes()
    {
        gazeCalibrationRequested = true;
    }

    public static Vector2 GazeAngles(Vector2 iris, Vector2 neutral, float horizontalRange,
        float verticalRange, float maxYaw, float maxPitch, bool invertHorizontal, bool invertVertical,
        float horizontalSensitivity = 1, float verticalSensitivity = 1)
    {
        float yaw = Mathf.Clamp((iris.x - neutral.x) * Mathf.Clamp(horizontalSensitivity, 0, 20) / Mathf.Max(0.01f, horizontalRange), -1, 1) * maxYaw;
        float pitch = Mathf.Clamp((iris.y - neutral.y) * Mathf.Clamp(verticalSensitivity, 0, 20) / Mathf.Max(0.01f, verticalRange), -1, 1) * maxPitch;
        return new Vector2(invertVertical ? -pitch : pitch, invertHorizontal ? -yaw : yaw);
    }

    void EyeMovement()
    {
        if (!enableEyeTracking)
        {
            if (gazeWasDriven)
            {
                RestoreEyeSurfaces();
                if (rotateLeftEye && leftEye != null) leftEye.localRotation = leftEyeRest;
                if (rotateRightEye && rightEye != null) rightEye.localRotation = rightEyeRest;
                filteredGazeAngles = Vector2.zero;
                gazeWasDriven = false;
            }
            return;
        }
        if (!rotateLeftEye && !rotateRightEye && leftSurface == null && rightSurface == null) return;
        Vector2 target = filteredGazeAngles;
        bool fresh = hasFaceData && Time.time - lastFaceTime < 0.5f;
        if (previewEyeMovement)
        {
            target = new Vector2(Mathf.Cos(Time.unscaledTime * 0.9f) * maximumEyePitch,
                Mathf.Sin(Time.unscaledTime * 1.2f) * maximumEyeYaw);
            SetEyes(0); // Make the texture sweep visible even with no camera connected.
        }
        else if (!fresh) target = Vector2.zero;
        else
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            if (UsableIris(x_ratio_left, y_ratio_left, ear_left))
            { sum += new Vector2(x_ratio_left, y_ratio_left); count++; }
            if (UsableIris(x_ratio_right, y_ratio_right, ear_right))
            { sum += new Vector2(x_ratio_right, y_ratio_right); count++; }
            // During a blink keep the previous gaze; closed-eye iris estimates
            // are unreliable. A lost face instead eases the eyes to neutral.
            if (count > 0)
            {
                Vector2 iris = sum / count;
                if (!gazeCalibrated || gazeCalibrationRequested)
                {
                    if (calibrateEyesOnFirstSample || gazeCalibrationRequested) gazeNeutral = iris;
                    gazeCalibrated = true;
                    gazeCalibrationRequested = false;
                }
                Vector2 angles = GazeAngles(iris, gazeNeutral, irisHorizontalRange, irisVerticalRange,
                    maximumEyeYaw, maximumEyePitch, invertEyeHorizontal, invertEyeVertical,
                    gazeSensitivity.x, gazeSensitivity.y);
                target = angles;
            }
        }
        float blend = eyeSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-eyeSmoothing * Time.deltaTime);
        filteredGazeAngles = Vector2.Lerp(filteredGazeAngles, target, blend);
        if (rotateLeftEye) ApplyEye(leftEye, leftEyeRest, leftEyeFrame);
        if (rotateRightEye) ApplyEye(rightEye, rightEyeRest, rightEyeFrame);
        ApplyEyeSurface(leftSurface, GazeUvOffset(filteredGazeAngles, maximumEyeYaw, maximumEyePitch,
            maximumEyeUvOffset, leftEyeUvDirection));
        ApplyEyeSurface(rightSurface, GazeUvOffset(filteredGazeAngles, maximumEyeYaw, maximumEyePitch,
            maximumEyeUvOffset, rightEyeUvDirection));
        gazeWasDriven = true;
    }

    bool UsableIris(float x, float y, float ear)
    {
        return ear >= gazeBlinkThreshold && x >= -0.25f && x <= 1.25f && y >= -0.25f && y <= 1.25f;
    }

    void ApplyEye(Transform eye, Quaternion rest, Quaternion frame)
    {
        if (eye != null) eye.localRotation = frame * Quaternion.Euler(filteredGazeAngles.x, filteredGazeAngles.y, 0)
            * Quaternion.Inverse(frame) * rest;
    }

    void EyeBlinking()
    {
        float ear_min = Mathf.Min(ear_left, ear_right);
        ear_min = Mathf.Clamp(ear_min, ear_min_threshold, ear_max_threshold);
        float x = Mathf.Abs((ear_min - ear_min_threshold) / (ear_max_threshold - ear_min_threshold) - 1);
        // Existing Unity-Chan eyelid blend-shape response curve.
        float y = 90 * Mathf.Pow(x, 2) - 5 * x;
        SetEyes(y);
    }

    void SetEyes(float ratio) {
        if (ref_SMR_EYE_DEF != null) ref_SMR_EYE_DEF.SetBlendShapeWeight(6, ratio);
        if (ref_SMR_EL_DEF != null) ref_SMR_EL_DEF.SetBlendShapeWeight(6, ratio);
    }

    public void EnableAutoBlink(bool enabled)
    {
        if (autoBlinkScript != null) autoBlinkScript.enabled = enabled;
        isAutoBlinkActive = enabled;
    }

    void MouthMoving() {
        float mar_clamped = Mathf.Clamp(mar, mar_min_threshold, mar_max_threshold);
        float ratio = (mar_clamped - mar_min_threshold) / (mar_max_threshold - mar_min_threshold);
        // enlarge it to [0, 100]
        ratio = ratio * 100 / (mar_max_threshold - mar_min_threshold);
        SetMouth(ratio);
    }

    void SetMouth(float ratio)
    {
        ref_SMR_MTH_DEF.SetBlendShapeWeight(2, ratio);
    }

    void OnDisable()
    {
        RestoreEyeSurfaces();
    }

    void OnDestroy()
    {
        RestoreEyeSurfaces();
        stopping = true;
        if (listener != null) listener.Stop();
        lock (faceGate) { if (client != null) client.Close(); }
        if (receiveThread != null) receiveThread.Join(1000);
    }

    // Save Load Interface Implementation
    // Ref: https://youtu.be/uD7y4T4PVk0
    public void PopulateSaveData(UnityChanPref unityChanPref)
    {
        unityChanPref.max_rotation_angle = max_rotation_angle;
        unityChanPref.ear_max_threshold = ear_max_threshold;
        unityChanPref.ear_min_threshold = ear_min_threshold;
        unityChanPref.isAutoBlinkActive = isAutoBlinkActive;
        unityChanPref.mar_max_threshold = mar_max_threshold;
        unityChanPref.mar_min_threshold = mar_min_threshold;
    }

    public void LoadFromSaveData(UnityChanPref unityChanPref)
    {
        max_rotation_angle = unityChanPref.max_rotation_angle;
        ear_max_threshold = unityChanPref.ear_max_threshold;
        ear_min_threshold = unityChanPref.ear_min_threshold;
        isAutoBlinkActive = unityChanPref.isAutoBlinkActive;
        mar_max_threshold = unityChanPref.mar_max_threshold;
        mar_min_threshold = unityChanPref.mar_min_threshold;
    }
}
