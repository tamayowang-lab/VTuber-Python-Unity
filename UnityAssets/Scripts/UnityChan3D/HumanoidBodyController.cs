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
    [Min(0)] public float occlusionGracePeriod = 0.2f;
    public float trackingTimeout = 0.5f;

    [Serializable] public class Point
    {
        public float x, y, z, visibility;
    }
    [Serializable] public class Packet
    {
        public int version;
        public Point[] pose, leftHand, rightHand;
    }
    class Bone
    {
        public Transform transform;
        public Quaternion restLocal, restRotation, filteredRotation;
        public Vector3 restDirection;
        public float lastTracked = float.NegativeInfinity;
    }

    readonly Dictionary<HumanBodyBones, Bone> bones = new Dictionary<HumanBodyBones, Bone>();
    readonly object gate = new object();
    TcpListener listener;
    TcpClient client;
    Thread worker;
    volatile bool stopping;
    Packet pending;
    Packet current;
    string networkError;
    float lastPacket = float.NegativeInfinity;
    Quaternion restTorsoFrame;
    readonly Dictionary<bool, Quaternion> restPalmFrames = new Dictionary<bool, Quaternion>();
    Animator animator;

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
        Add(HumanBodyBones.Spine, HumanBodyBones.Chest);
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
        restTorsoFrame = Frame(transform.InverseTransformDirection(right.position - left.position),
                               transform.InverseTransformDirection(neck.position - hips.position));
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
            filteredRotation = Quaternion.Inverse(transform.rotation) * bone.rotation,
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
                    filteredRotation = Quaternion.Inverse(transform.rotation) * tip.rotation,
                    restDirection = previous.restDirection };
        }
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
            && ValidPoints(p.leftHand, 21) && ValidPoints(p.rightHand, 21);
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

    float Blend { get { return 1f - Mathf.Exp(-Mathf.Max(0, smoothing) * Time.deltaTime); } }
    void Aim(HumanBodyBones id, Vector3 direction)
    {
        Bone bone;
        if (!bones.TryGetValue(id, out bone) || direction.sqrMagnitude < 1e-10f) return;
        Rotate(bone, Quaternion.FromToRotation(bone.restDirection, direction) * bone.restRotation);
    }
    void Rotate(Bone bone, Quaternion target)
    {
        // Ignore small landmark noise and cap sudden monocular depth flips.
        if (Quaternion.Angle(bone.filteredRotation, target) > rotationDeadZone)
        {
            Quaternion smooth = Quaternion.Slerp(bone.filteredRotation, target, Blend);
            bone.filteredRotation = Quaternion.RotateTowards(bone.filteredRotation, smooth,
                Mathf.Max(1, maximumAngularSpeed) * Time.deltaTime);
        }
        bone.lastTracked = Time.time;
        bone.transform.rotation = transform.rotation * bone.filteredRotation;
    }
    void HoldOrResetBone(HumanBodyBones id)
    {
        Bone bone;
        if (!bones.TryGetValue(id, out bone)) return;
        if (Time.time - bone.lastTracked <= occlusionGracePeriod)
            bone.transform.rotation = transform.rotation * bone.filteredRotation;
        else ResetBone(id);
    }
    void ResetBone(HumanBodyBones id)
    {
        Bone bone;
        if (bones.TryGetValue(id, out bone))
        {
            bone.transform.localRotation = Quaternion.Slerp(bone.transform.localRotation, bone.restLocal, Blend);
            bone.filteredRotation = Quaternion.Inverse(transform.rotation) * bone.transform.rotation;
        }
    }

    void LateUpdate()
    {
        lock (gate)
        {
            if (pending != null) { current = pending; pending = null; lastPacket = Time.time; }
            if (networkError != null) { Debug.LogWarning(networkError, this); networkError = null; }
        }
        if (current == null || Time.time - lastPacket > trackingTimeout)
        {
            foreach (HumanBodyBones id in bones.Keys) ResetBone(id);
            return;
        }
        Point[] p = current.pose;
        if (Visible(p, 11, 12, 23, 24))
        {
            Vector3 right = TrackingPosition(p[24]) - TrackingPosition(p[23]);
            Vector3 up = (TrackingPosition(p[11]) + TrackingPosition(p[12]) - TrackingPosition(p[23]) - TrackingPosition(p[24])) * 0.5f;
            if (Vector3.Cross(right, up).sqrMagnitude > 1e-10f)
            {
                Bone hips;
                if (bones.TryGetValue(HumanBodyBones.Hips, out hips))
                    Rotate(hips, Frame(right, up) * Quaternion.Inverse(restTorsoFrame) * hips.restRotation);
            }
            Vector3 shoulderRight = TrackingPosition(p[12]) - TrackingPosition(p[11]);
            Bone spine;
            if (Vector3.Cross(shoulderRight, up).sqrMagnitude > 1e-10f
                && bones.TryGetValue(HumanBodyBones.Spine, out spine))
                Rotate(spine, Frame(shoulderRight, up) * Quaternion.Inverse(restTorsoFrame) * spine.restRotation);
        }
        else { ResetBone(HumanBodyBones.Hips); ResetBone(HumanBodyBones.Spine); }
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
        if (Visible(p, shoulder, elbow)) Aim(upper, TrackingPosition(p[elbow]) - TrackingPosition(p[shoulder]));
        else HoldOrResetBone(upper);
        if (Visible(p, elbow, wrist)) Aim(lower, TrackingPosition(p[wrist]) - TrackingPosition(p[elbow]));
        else HoldOrResetBone(lower);
        if (hand == null || hand.Length != 21)
        {
            HoldOrResetBone(palm);
            foreach (HumanBodyBones id in fingers) HoldOrResetBone(id);
            return;
        }
        Vector3 palmRight = TrackingPosition(hand[5]) - TrackingPosition(hand[17]);
        Vector3 palmUp = TrackingPosition(hand[9]) - TrackingPosition(hand[0]);
        Bone palmBone;
        Quaternion restFrame;
        if (Vector3.Cross(palmRight, palmUp).sqrMagnitude > 1e-10f
            && bones.TryGetValue(palm, out palmBone) && restPalmFrames.TryGetValue(left, out restFrame))
            Rotate(palmBone, Frame(palmRight, palmUp) * Quaternion.Inverse(restFrame) * palmBone.restRotation);
        else Aim(palm, palmUp);
        for (int finger = 0; finger < 5; finger++)
            for (int segment = 0; segment < 3; segment++)
            {
                int start = 1 + finger * 4 + segment;
                Aim(fingers[finger * 3 + segment], TrackingPosition(hand[start + 1]) - TrackingPosition(hand[start]));
            }
    }

    void OnDestroy()
    {
        stopping = true;
        if (listener != null) listener.Stop();
        lock (gate) { if (client != null) client.Close(); }
        if (worker != null) worker.Join(1000);
    }
}
