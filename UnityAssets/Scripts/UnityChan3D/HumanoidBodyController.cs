using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Add alongside the existing face controller. Requires a Humanoid Avatar.
// Transport: one version-1 JSON object per newline, on a separate TCP port.
[DefaultExecutionOrder(100)]
public class HumanoidBodyController : MonoBehaviour
{
    public int port = 5067;
    [Range(0, 1)] public float minimumVisibility = 0.6f;
    public float smoothing = 8f;
    [Tooltip("Reverse camera depth so reaching toward the camera moves toward the avatar front.")]
    public bool invertDepth = true;
    [Min(1)] public float maximumAngularSpeed = 240f;
    [Range(0, 5)] public float rotationDeadZone = 1f;
    [Min(0)] public float occlusionGracePeriod = 0.4f;
    public bool returnToRestWhenLost = true;
    public bool useRelaxedArmRestPose = true;
    [Range(0, 35)] public float restingArmSideAngle = 18f;
    [Range(0, 45)] public float restingElbowBend = 18f;
    public float trackingTimeout = 0.5f;
    [Tooltip("Require a detailed hand detection before driving that arm; avoids inferred wrist jitter.")]
    public bool requireHandForArmTracking = false;
    public bool inferArmFromHand = true;
    public bool limitElbowInward = true;
    [Tooltip("Minimum elbow distance from the torso midline, as a fraction of shoulder width. Higher keeps elbows farther out.")]
    [Range(0, 0.65f)] public float elbowSideClearance = 0.35f;
    [Range(0, 80)] public float maximumTorsoAngle = 40f;
    [Tooltip("With hips out of view, estimate torso turn/side tilt from shoulders only; forward lean needs hips.")]
    public bool allowShoulderOnlyTorso = true;
    [Min(0)] public float fingerSmoothing = 5f;
    public enum DepthMotionMode { Disabled, UpperBodyLean, WholeAvatar }
    [Tooltip("UpperBodyLean preserves the fixed pelvis; WholeAvatar moves the model root.")]
    public DepthMotionMode depthMotion = DepthMotionMode.Disabled;
    [Range(0, 40)] public float maximumDepthLean = 18f;
    [Min(0)] public float depthTranslationScale = 1f;
    [Min(0)] public float maximumDepthTranslation = 0.3f;
    [Min(0)] public float depthSmoothing = 3f;
    [SerializeField] bool torsoTracking;
    public bool TorsoTracking { get { return torsoTracking; } }

    [Serializable] public class Point
    {
        public float x, y, z, visibility;
    }
    [Serializable] public class Packet
    {
        public int version;
        public Point[] pose, leftHand, rightHand, imagePose;
        public bool depthValid;
        public float depthOffset;
    }
    class Bone
    {
        public Transform transform;
        public Quaternion restLocal, neutralLocal, restRotation, filteredLocal, avatarToParent;
        public bool ownsPose, finger;
        public Vector3 restDirection;
        public float lastTracked = float.NegativeInfinity;
    }

    class ArmGeometry
    {
        public float upperLength, lowerLength;
    }
    readonly Dictionary<bool, ArmGeometry> armGeometry = new Dictionary<bool, ArmGeometry>();
    float avatarShoulderSpan;
    readonly Dictionary<HumanBodyBones, Bone> bones = new Dictionary<HumanBodyBones, Bone>();
    readonly object gate = new object();
    TcpListener listener;
    TcpClient client;
    Thread worker;
    volatile bool stopping;
    Packet pending;
    Packet current;
    bool trackingActive;
    string networkError;
    float lastPacket = float.NegativeInfinity;
    Quaternion restTorsoFrame;
    readonly Dictionary<bool, Quaternion> restPalmFrames = new Dictionary<bool, Quaternion>();
    Animator animator;
    Transform hipTransform;
    Vector3 hipRestPosition;
    Vector3 rootRestPosition, rootForwardInParent;
    float filteredDepth, rootDepth;
    bool ownsRootDepth;
    readonly List<HumanBodyBones> torsoBones = new List<HumanBodyBones>();

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("HumanoidBodyController requires a Humanoid Animator.", this);
            enabled = false;
            return;
        }
        Add(HumanBodyBones.Hips, HumanBodyBones.Spine);
        // Chest and UpperChest are optional Humanoid mappings. Always drive
        // the mapped spine, even when the avatar has no mapped Chest.
        HumanBodyBones[] chain = { HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.UpperChest, HumanBodyBones.Neck };
        for (int i = 0; i < chain.Length - 1; i++)
        {
            if (animator.GetBoneTransform(chain[i]) == null) continue;
            for (int j = i + 1; j < chain.Length; j++)
                if (animator.GetBoneTransform(chain[j]) != null)
                {
                    Add(chain[i], chain[j]);
                    if (bones.ContainsKey(chain[i])) torsoBones.Add(chain[i]);
                    break;
                }
        }
        AddArm(true);
        AddArm(false);
        Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        if (left == null || right == null || hips == null || neck == null)
        {
            Debug.LogError("Humanoid Avatar needs hips, neck and both arms.", this);
            enabled = false;
            return;
        }
        avatarShoulderSpan = (transform.InverseTransformPoint(right.position)
            - transform.InverseTransformPoint(left.position)).magnitude;
        hipTransform = hips;
        hipRestPosition = hips.localPosition;
        rootRestPosition = transform.localPosition;
        rootForwardInParent = transform.parent == null ? transform.forward
            : transform.parent.InverseTransformVector(transform.forward);
        if (torsoBones.Count == 0)
            Debug.LogWarning("No usable Humanoid spine mapping; check Configure Avatar.", this);
        restTorsoFrame = Frame(transform.InverseTransformDirection(right.position - left.position),
                               transform.InverseTransformDirection(neck.position - hips.position));
        ConfigureRelaxedArm(true);
        ConfigureRelaxedArm(false);
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            worker = new Thread(Receive) { IsBackground = true };
            worker.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("Body tracking listener: " + e.Message, this);
            enabled = false;
        }
    }

    void Add(HumanBodyBones id, HumanBodyBones end)
    {
        Transform bone = animator.GetBoneTransform(id);
        Transform child = animator.GetBoneTransform(end);
        if (bone == null || child == null) return;
        Vector3 direction = transform.InverseTransformDirection(child.position - bone.position);
        if (direction.sqrMagnitude < 1e-10f) return;
        bones[id] = new Bone { transform = bone, restLocal = bone.localRotation,
            restRotation = Quaternion.Inverse(transform.rotation) * bone.rotation,
            neutralLocal = bone.localRotation,
            filteredLocal = bone.localRotation,
            avatarToParent = Quaternion.Inverse(bone.parent.rotation) * transform.rotation,
            restDirection = direction.normalized };
    }

    // Explicit tables avoid depending on the numeric order of Unity's enum.
    static readonly HumanBodyBones[] LeftFingers = {
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal };
    static readonly HumanBodyBones[] RightFingers = {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };

    void AddArm(bool left)
    {
        HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
        HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        HumanBodyBones[] fingers = left ? LeftFingers : RightFingers;
        Add(upper, lower); Add(lower, hand); Add(hand, fingers[6]);
        Transform upperTransform = animator.GetBoneTransform(upper);
        Transform lowerTransform = animator.GetBoneTransform(lower);
        Transform handTransform = animator.GetBoneTransform(hand);
        if (upperTransform != null && lowerTransform != null && handTransform != null)
            armGeometry[left] = new ArmGeometry {
                upperLength = (transform.InverseTransformPoint(lowerTransform.position)
                    - transform.InverseTransformPoint(upperTransform.position)).magnitude,
                lowerLength = (transform.InverseTransformPoint(handTransform.position)
                    - transform.InverseTransformPoint(lowerTransform.position)).magnitude };
        Transform wrist = animator.GetBoneTransform(hand);
        Transform index = animator.GetBoneTransform(fingers[3]);
        Transform little = animator.GetBoneTransform(fingers[12]);
        Transform middle = animator.GetBoneTransform(fingers[6]);
        if (wrist != null && index != null && little != null && middle != null)
            restPalmFrames[left] = Frame(transform.InverseTransformDirection(index.position - little.position),
                transform.InverseTransformDirection(middle.position - wrist.position));
        for (int i = 0; i < fingers.Length; i += 3)
        {
            Add(fingers[i], fingers[i + 1]);
            Add(fingers[i + 1], fingers[i + 2]);
            Transform tip = animator.GetBoneTransform(fingers[i + 2]);
            Bone previous;
            // Humanoid has no fingertip bone. Use the preceding segment's axis.
            if (tip != null && bones.TryGetValue(fingers[i + 1], out previous))
                bones[fingers[i + 2]] = new Bone { transform = tip, restLocal = tip.localRotation,
                    restRotation = Quaternion.Inverse(transform.rotation) * tip.rotation,
                    neutralLocal = tip.localRotation,
                    filteredLocal = tip.localRotation,
                    avatarToParent = Quaternion.Inverse(tip.parent.rotation) * transform.rotation,
                    restDirection = previous.restDirection };
        }
        foreach (HumanBodyBones id in fingers)
        {
            Bone finger;
            if (bones.TryGetValue(id, out finger)) finger.finger = true;
        }
    }

    void ConfigureRelaxedArm(bool left)
    {
        Bone upper, lower;
        if (!bones.TryGetValue(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, out upper)
            || !bones.TryGetValue(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, out lower)) return;
        float side = left ? -1f : 1f;
        float angle = restingArmSideAngle * Mathf.Deg2Rad;
        float bend = restingElbowBend * Mathf.Deg2Rad;
        Vector3 upperDirection = new Vector3(side * Mathf.Sin(angle), -Mathf.Cos(angle), 0);
        Vector3 lowerDirection = new Vector3(side * Mathf.Sin(angle), -Mathf.Cos(angle), Mathf.Sin(bend)).normalized;
        Quaternion upperRoot = Quaternion.FromToRotation(upper.restDirection, upperDirection) * upper.restRotation;
        Quaternion lowerRoot = Quaternion.FromToRotation(lower.restDirection, lowerDirection) * lower.restRotation;
        Quaternion parentRoot = Quaternion.Inverse(transform.rotation) * upper.transform.parent.rotation;
        upper.neutralLocal = Quaternion.Inverse(parentRoot) * upperRoot;
        // Preserve any intervening forearm/twist transform in the rig hierarchy.
        Quaternion intervening = Quaternion.Inverse(upper.transform.rotation) * lower.transform.parent.rotation;
        lower.neutralLocal = Quaternion.Inverse(upperRoot * intervening) * lowerRoot;
    }

    bool IsArmBone(HumanBodyBones id)
    {
        return id == HumanBodyBones.LeftUpperArm || id == HumanBodyBones.LeftLowerArm
            || id == HumanBodyBones.RightUpperArm || id == HumanBodyBones.RightLowerArm;
    }

    void RelaxIdleArms()
    {
        // Use our cached pose, not Animator's T-pose on each frame.
        foreach (HumanBodyBones id in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand })
            ResetBone(id);
        foreach (HumanBodyBones id in LeftFingers) ResetBone(id);
        foreach (HumanBodyBones id in RightFingers) ResetBone(id);
    }

    void Receive()
    {
        try
        {
            while (!stopping)
            {
                TcpClient accepted = listener.AcceptTcpClient();
                lock (gate) { if (stopping) { accepted.Close(); return; } client = accepted; }
                try
                {
                    using (accepted)
                    using (NetworkStream stream = accepted.GetStream())
                    {
                        byte[] buffer = new byte[4096];
                        StringBuilder line = new StringBuilder();
                        int count;
                        while (!stopping && (count = stream.Read(buffer, 0, buffer.Length)) > 0)
                            for (int i = 0; i < count; i++)
                            {
                                if (buffer[i] == 10)
                                {
                                    try
                                    {
                                        Packet packet = JsonUtility.FromJson<Packet>(line.ToString());
                                        if (Valid(packet)) lock (gate) pending = packet;
                                    }
                                    catch (ArgumentException) { /* Ignore malformed frames. */ }
                                    line.Length = 0;
                                }
                                else if (line.Length < 32768) line.Append((char)buffer[i]);
                                else throw new IOException("Body tracking frame exceeds 32 KB");
                            }
                    }
                }
                catch (IOException) { /* A disconnected sender may reconnect. */ }
                catch (SocketException) { }
                finally { lock (gate) client = null; }
            }
        }
        catch (Exception e) { if (!stopping) lock (gate) networkError = e.Message; }
    }

    static bool ValidPoints(Point[] points, int count)
    {
        if (points == null || (points.Length != 0 && points.Length != count)) return false;
        foreach (Point p in points)
            if (p == null || !Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(p.visibility)) return false;
        return true;
    }
    static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    static bool Valid(Packet p)
    {
        return p != null && p.version == 1 && ValidPoints(p.pose, 33)
            && ValidPoints(p.leftHand, 21) && ValidPoints(p.rightHand, 21)
            && (p.imagePose == null || ValidPoints(p.imagePose, 33))
            && (!p.depthValid || (Finite(p.depthOffset) && Mathf.Abs(p.depthOffset) <= 0.5f));
    }
    static Quaternion Frame(Vector3 right, Vector3 up)
    {
        return Quaternion.LookRotation(Vector3.Cross(right, up).normalized, up.normalized);
    }
    bool Visible(Point[] p, params int[] indices)
    {
        if (p == null || p.Length != 33) return false;
        foreach (int i in indices) if (p[i].visibility < minimumVisibility) return false;
        return true;
    }
    Vector3 TrackingPosition(Point point)
    {
        return new Vector3(point.x, point.y, invertDepth ? -point.z : point.z);
    }

    void Aim(HumanBodyBones id, Vector3 direction)
    {
        Bone bone;
        if (!bones.TryGetValue(id, out bone) || direction.sqrMagnitude < 1e-10f) return;
        Rotate(bone, Quaternion.FromToRotation(bone.restDirection, direction) * bone.restRotation);
    }
    void Rotate(Bone bone, Quaternion target)
    {
        Quaternion localTarget = Quaternion.Inverse(bone.transform.parent.rotation)
            * transform.rotation * target;
        ApplyLocal(bone, localTarget, Time.deltaTime, true);
        bone.lastTracked = Time.time;
    }

    // Filter our OWN local pose, never the Animator's newly-written pose.
    // This lets a held arm follow its parent torso without snapping or drifting.
    void ApplyLocal(Bone bone, Quaternion target, float deltaTime, bool deadZone)
    {
        if (!deadZone || Quaternion.Angle(bone.filteredLocal, target) > rotationDeadZone)
        {
            float response = bone.finger ? fingerSmoothing : smoothing;
            float blend = response <= 0 ? 1 : 1f - Mathf.Exp(-response * deltaTime);
            Quaternion smooth = Quaternion.Slerp(bone.filteredLocal, target, blend);
            bone.filteredLocal = Quaternion.RotateTowards(bone.filteredLocal, smooth,
                Mathf.Max(1, maximumAngularSpeed) * deltaTime);
        }
        bone.transform.localRotation = bone.filteredLocal;
        bone.ownsPose = true;
    }

    void HoldOrResetBone(HumanBodyBones id)
    {
        Bone bone;
        if (!bones.TryGetValue(id, out bone)) return;
        if (!returnToRestWhenLost || Time.time - bone.lastTracked <= occlusionGracePeriod)
        {
            bone.transform.localRotation = bone.filteredLocal;
            bone.ownsPose = true;
        }
        else ResetBone(id);
    }
    void ResetBone(HumanBodyBones id)
    {
        Bone bone;
        if (bones.TryGetValue(id, out bone))
            ApplyLocal(bone, useRelaxedArmRestPose && IsArmBone(id)
                ? bone.neutralLocal : bone.restLocal, Time.deltaTime, false);
    }

    void ApplyTorso(Point[] p, float deltaTime)
    {
        bool hipsVisible = Visible(p, 11, 12, 23, 24);
        torsoTracking = Visible(p, 11, 12) && (hipsVisible || allowShoulderOnlyTorso);
        if (!torsoTracking)
        {
            foreach (HumanBodyBones id in torsoBones) HoldOrResetBone(id);
            return;
        }
        Vector3 right = TrackingPosition(p[12]) - TrackingPosition(p[11]);
        Vector3 up = hipsVisible
            ? (TrackingPosition(p[11]) + TrackingPosition(p[12])
                - TrackingPosition(p[23]) - TrackingPosition(p[24])) * 0.5f
            : Vector3.ProjectOnPlane(Vector3.up, right.normalized);
        if (Vector3.Cross(right, up).sqrMagnitude < 1e-10f)
        {
            torsoTracking = false;
            foreach (HumanBodyBones id in torsoBones) HoldOrResetBone(id);
            return;
        }
        Quaternion delta = Frame(right, up) * Quaternion.Inverse(restTorsoFrame);
        if (depthMotion == DepthMotionMode.UpperBodyLean)
            delta = Quaternion.AngleAxis(filteredDepth * 2f * maximumDepthLean, Vector3.right) * delta;
        delta = Quaternion.RotateTowards(Quaternion.identity, delta, maximumTorsoAngle);
        // Distribute lean/twist over the available spine chain. Do not rotate
        // or translate the pelvis: the lower body remains anchored.
        float weight = 1f / Mathf.Max(1, torsoBones.Count);
        foreach (HumanBodyBones id in torsoBones)
        {
            Bone bone = bones[id];
            Quaternion portion = Quaternion.Slerp(Quaternion.identity, delta, weight);
            Quaternion target = bone.avatarToParent * portion
                * Quaternion.Inverse(bone.avatarToParent) * bone.restLocal;
            ApplyLocal(bone, target, deltaTime, true);
            bone.lastTracked = Time.time;
        }
    }

    void ApplyDepthMotion(bool live, float deltaTime)
    {
        float blend = depthSmoothing <= 0 ? 1 : 1f - Mathf.Exp(-depthSmoothing * deltaTime);
        float target = live && current.depthValid && depthMotion != DepthMotionMode.Disabled
            ? current.depthOffset : 0;
        filteredDepth = Mathf.Lerp(filteredDepth, target, blend);
        float rootTarget = depthMotion == DepthMotionMode.WholeAvatar
            ? Mathf.Clamp(filteredDepth * depthTranslationScale,
                -maximumDepthTranslation, maximumDepthTranslation) : 0;
        rootDepth = Mathf.Lerp(rootDepth, rootTarget, blend);
        if (depthMotion == DepthMotionMode.WholeAvatar || ownsRootDepth)
        {
            transform.localPosition = rootRestPosition + rootForwardInParent * rootDepth;
            ownsRootDepth = Mathf.Abs(rootDepth) > 0.0001f;
        }
    }

    void LateUpdate()
    {
        lock (gate)
        {
            if (pending != null) { current = pending; pending = null; lastPacket = Time.time; }
            if (networkError != null) { Debug.LogWarning(networkError, this); networkError = null; }
        }
        bool live = current != null && Time.time - lastPacket <= trackingTimeout;
        ApplyDepthMotion(live, Time.deltaTime);
        // With no input, leave the torso alone. Optionally own only the arms
        // to maintain the requested relaxed rest pose from our cached filter.
        if (!live)
        {
            trackingActive = false;
            torsoTracking = false;
            foreach (Bone bone in bones.Values) bone.ownsPose = false;
            if (useRelaxedArmRestPose) RelaxIdleArms();
            return;
        }
        if (!trackingActive)
        {
            // Start/reconnect from the actual animated pose, not an old target.
            foreach (Bone bone in bones.Values)
            {
                if (!bone.ownsPose) bone.filteredLocal = bone.transform.localRotation;
                bone.lastTracked = float.NegativeInfinity;
            }
            trackingActive = true;
        }
        // Pin only while this component owns a live tracking session.
        hipTransform.localPosition = hipRestPosition;
        Bone hips;
        if (bones.TryGetValue(HumanBodyBones.Hips, out hips))
            ApplyLocal(hips, hips.restLocal, Time.deltaTime, false);
        Point[] p = current.pose;
        ApplyTorso(p, Time.deltaTime);
        ApplyArm(true, p, current.leftHand);
        ApplyArm(false, p, current.rightHand);
    }

    void ApplyArm(bool left, Point[] p, Point[] hand)
    {
        int shoulder = left ? 11 : 12, elbow = left ? 13 : 14, wrist = left ? 15 : 16;
        HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
        HumanBodyBones palm = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        HumanBodyBones[] fingers = left ? LeftFingers : RightFingers;
        bool handPresent = hand != null && hand.Length == 21;
        bool armReliable = (!requireHandForArmTracking || handPresent)
            && Visible(p, shoulder, elbow, wrist);
        bool inferred = !armReliable && handPresent && inferArmFromHand
            && TryInferArm(left, current == null ? null : current.imagePose, hand);
        if (armReliable)
        {
            DriveTrackedArm(left, TrackingPosition(p[elbow]) - TrackingPosition(p[shoulder]),
                TrackingPosition(p[wrist]) - TrackingPosition(p[elbow]));
        }
        else if (!inferred)
        {
            HoldOrResetBone(upper);
            HoldOrResetBone(lower);
            HoldOrResetBone(palm);
            foreach (HumanBodyBones id in fingers) HoldOrResetBone(id);
            return;
        }
        if (!handPresent)
        {
            // A neutral local wrist follows the reliable forearm naturally.
            // Open the fingers instead of freezing a previous grip.
            ResetBone(palm);
            foreach (HumanBodyBones id in fingers) ResetBone(id);
            return;
        }
        Vector3 palmRight = TrackingPosition(hand[5]) - TrackingPosition(hand[17]);
        Vector3 palmUp = TrackingPosition(hand[9]) - TrackingPosition(hand[0]);
        Bone palmBone;
        Quaternion restFrame;
        bool palmReliable = palmUp.sqrMagnitude > 1e-8f
            && Vector3.Cross(palmRight, palmUp).sqrMagnitude > 1e-10f;
        if (palmReliable && bones.TryGetValue(palm, out palmBone) && restPalmFrames.TryGetValue(left, out restFrame))
            Rotate(palmBone, Frame(palmRight, palmUp) * Quaternion.Inverse(restFrame) * palmBone.restRotation);
        else ResetBone(palm);
        float palmSize = Mathf.Max(palmRight.magnitude, palmUp.magnitude);
        for (int finger = 0; finger < 5; finger++)
        {
            bool reliable = palmReliable && FingerReliable(hand, finger, palmSize);
            for (int segment = 0; segment < 3; segment++)
            {
                HumanBodyBones id = fingers[finger * 3 + segment];
                int start = 1 + finger * 4 + segment;
                if (reliable) Aim(id, TrackingPosition(hand[start + 1]) - TrackingPosition(hand[start]));
                else ResetBone(id);
            }
        }
    }

    // Analytic two-bone IK with fixed bone lengths. The wrist target is
    // projected from the image, NOT assigned the hand model's relative z.
    public static bool SolveInferredArm(Vector3 shoulder, Vector3 desiredWrist,
        float upperLength, float lowerLength, Vector3 pole, out Vector3 elbow, out Vector3 wrist)
    {
        elbow = wrist = shoulder;
        if (upperLength < 1e-5f || lowerLength < 1e-5f) return false;
        Vector3 delta = desiredWrist - shoulder;
        float total = upperLength + lowerLength;
        if (delta.sqrMagnitude < 1e-10f) return false;
        Vector3 direction = delta.normalized;
        float minimumReach = Mathf.Abs(upperLength - lowerLength) + total * 0.05f;
        float maximumReach = total * 0.95f;
        if (minimumReach >= maximumReach) return false;
        float distance = Mathf.Clamp(delta.magnitude, minimumReach, maximumReach);
        wrist = shoulder + direction * distance;
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);
        bend.Normalize();
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance)
            / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
        elbow = shoulder + direction * along + bend * height;
        return true;
    }

    bool TryInferArm(bool left, Point[] imagePose, Point[] hand)
    {
        // Without a reliable body anchor/scale, hold rather than invent an arm.
        if (!Visible(imagePose, 11, 12) || avatarShoulderSpan < 1e-5f) return false;
        ArmGeometry geometry;
        Bone upper;
        HumanBodyBones upperId = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        if (!armGeometry.TryGetValue(left, out geometry) || !bones.TryGetValue(upperId, out upper)) return false;
        Vector3 imageLeft = new Vector3(imagePose[11].x, imagePose[11].y, 0);
        Vector3 imageRight = new Vector3(imagePose[12].x, imagePose[12].y, 0);
        float imageSpan = (imageRight - imageLeft).magnitude;
        if (imageSpan < 0.05f) return false;
        Point imageShoulder = imagePose[left ? 11 : 12];
        float scale = avatarShoulderSpan / imageSpan;
        Vector3 shoulder = transform.InverseTransformPoint(upper.transform.position);
        float total = geometry.upperLength + geometry.lowerLength;
        Vector3 delta = new Vector3((hand[0].x - imageShoulder.x) * scale,
            (hand[0].y - imageShoulder.y) * scale, total * 0.15f);
        // Keep uncertain targets in front of the torso and limit body crossing.
        float side = left ? -1f : 1f;
        delta.x = side * Mathf.Clamp(side * delta.x, -total * 0.25f, total * 0.95f);
        delta.y = Mathf.Clamp(delta.y, -total * 0.95f, total * 0.9f);
        Vector3 elbow, wrist;
        if (!SolveInferredArm(shoulder, shoulder + delta, geometry.upperLength, geometry.lowerLength,
            new Vector3(side * 0.5f, -1, 0.5f), out elbow, out wrist)) return false;
        DriveArmTargets(left, shoulder, elbow, wrist, geometry);
        return true;
    }

    void DriveTrackedArm(bool left, Vector3 upperDirection, Vector3 lowerDirection)
    {
        HumanBodyBones upperId = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        HumanBodyBones lowerId = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
        ArmGeometry geometry;
        Bone upper;
        if (!armGeometry.TryGetValue(left, out geometry) || !bones.TryGetValue(upperId, out upper))
        {
            Aim(upperId, upperDirection);
            Aim(lowerId, lowerDirection);
            return;
        }
        if (upperDirection.sqrMagnitude < 1e-10f || lowerDirection.sqrMagnitude < 1e-10f)
        {
            HoldOrResetBone(upperId); HoldOrResetBone(lowerId);
            return;
        }
        Vector3 shoulder = transform.InverseTransformPoint(upper.transform.position);
        Vector3 elbow = shoulder + upperDirection.normalized * geometry.upperLength;
        Vector3 wrist = elbow + lowerDirection.normalized * geometry.lowerLength;
        DriveArmTargets(left, shoulder, elbow, wrist, geometry);
    }

    // Project an inward elbow outward without stretching either bone.
    // Wrist placement is adjusted when exact contact conflicts with clearance.
    public static bool ConstrainElbow(Vector3 shoulder, Vector3 elbow, Vector3 wrist,
        Vector3 torsoCenter, Vector3 torsoRight, bool left, float clearance,
        float upperLength, float lowerLength, out Vector3 safeElbow, out Vector3 safeWrist)
    {
        safeElbow = elbow; safeWrist = wrist;
        if (upperLength < 1e-5f || lowerLength < 1e-5f || torsoRight.sqrMagnitude < 1e-8f) return false;
        Vector3 outward = torsoRight.normalized * (left ? -1f : 1f);
        Vector3 direction = elbow - shoulder;
        if (direction.sqrMagnitude < 1e-10f) return false;
        direction.Normalize();
        float minimum = Mathf.Clamp((clearance - Vector3.Dot(shoulder - torsoCenter, outward))
            / upperLength, -0.95f, 0.95f);
        float actual = Vector3.Dot(direction, outward);
        if (actual < minimum)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(direction, outward);
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.ProjectOnPlane(Vector3.down, outward);
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.ProjectOnPlane(Vector3.forward, outward);
            direction = outward * minimum + tangent.normalized * Mathf.Sqrt(1 - minimum * minimum);
        }
        safeElbow = shoulder + direction * upperLength;
        Vector3 forearm = wrist - safeElbow;
        if (forearm.sqrMagnitude < 1e-10f) forearm = direction;
        safeWrist = safeElbow + forearm.normalized * lowerLength;
        return true;
    }

    bool ElbowFrame(out Vector3 center, out Vector3 right, out float clearance)
    {
        center = right = Vector3.zero; clearance = 0;
        Bone left, rightBone;
        if (!bones.TryGetValue(HumanBodyBones.LeftUpperArm, out left)
            || !bones.TryGetValue(HumanBodyBones.RightUpperArm, out rightBone)) return false;
        Vector3 a = transform.InverseTransformPoint(left.transform.position);
        Vector3 b = transform.InverseTransformPoint(rightBone.transform.position);
        right = b - a;
        if (right.sqrMagnitude < 1e-8f) return false;
        clearance = right.magnitude * elbowSideClearance;
        center = (a + b) * 0.5f;
        return true;
    }

    void DriveArmTargets(bool left, Vector3 shoulder, Vector3 elbow, Vector3 wrist, ArmGeometry geometry)
    {
        HumanBodyBones upperId = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
        HumanBodyBones lowerId = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
        Vector3 center = Vector3.zero, right = Vector3.zero;
        float clearance = 0;
        bool constrain = limitElbowInward && ElbowFrame(out center, out right, out clearance);
        // Explicit defaults keep these initialized when the optional limit is off.
        if (!constrain) { center = right = Vector3.zero; clearance = 0; }
        else ConstrainElbow(shoulder, elbow, wrist, center, right, left, clearance,
            geometry.upperLength, geometry.lowerLength, out elbow, out wrist);
        Aim(upperId, elbow - shoulder);
        Bone lower, upper;
        if (bones.TryGetValue(lowerId, out lower) && bones.TryGetValue(upperId, out upper))
        {
            Vector3 actualElbow = transform.InverseTransformPoint(lower.transform.position);
            if (constrain)
            {
                // Enforce the boundary on the filtered pose too, rather than
                // allowing a smoothing transition to pass through the chest.
                Vector3 correctedElbow, unusedWrist;
                if (ConstrainElbow(shoulder, actualElbow, wrist, center, right, left, clearance,
                    geometry.upperLength, geometry.lowerLength, out correctedElbow, out unusedWrist))
                {
                    Vector3 from = transform.TransformDirection(actualElbow - shoulder);
                    Vector3 to = transform.TransformDirection(correctedElbow - shoulder);
                    upper.transform.rotation = Quaternion.FromToRotation(from, to) * upper.transform.rotation;
                    upper.filteredLocal = upper.transform.localRotation;
                    actualElbow = transform.InverseTransformPoint(lower.transform.position);
                }
            }
            Aim(lowerId, wrist - actualElbow);
        }
        else Aim(lowerId, wrist - elbow);
    }

    bool FingerReliable(Point[] hand, int finger, float palmSize)
    {
        // Holistic supplies no per-finger confidence. Reject only implausible
        // or collapsed geometry; never let this decision disable the arm/palm.
        for (int segment = 0; segment < 3; segment++)
        {
            int start = 1 + finger * 4 + segment;
            float length = (TrackingPosition(hand[start + 1]) - TrackingPosition(hand[start])).magnitude;
            if (length < palmSize * 0.02f || length > palmSize * 1.2f) return false;
        }
        return palmSize > 1e-5f;
    }

    void OnDestroy()
    {
        stopping = true;
        if (listener != null) listener.Stop();
        lock (gate) { if (client != null) client.Close(); }
        if (worker != null) worker.Join(1000);
    }
}
