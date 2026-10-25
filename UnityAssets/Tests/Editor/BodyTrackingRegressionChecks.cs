// Copy into Assets/Editor with the controller to run without NUnit/asmdefs.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BodyTrackingRegressionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type BoneType = typeof(HumanoidBodyController).GetNestedType("Bone", BindingFlags.NonPublic);
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
    static void Set(object owner, string name, object value) { owner.GetType().GetField(name, Private).SetValue(owner, value); }
    static object Call(object owner, string name, params object[] args)
    {
        return owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static object AddBone(HumanoidBodyController controller, Transform joint, Quaternion initial)
    {
        object bone = Activator.CreateInstance(BoneType, true);
        BoneType.GetField("transform").SetValue(bone, joint);
        BoneType.GetField("restLocal").SetValue(bone, Quaternion.identity);
        BoneType.GetField("neutralLocal").SetValue(bone, Quaternion.identity);
        BoneType.GetField("restRotation").SetValue(bone, Quaternion.identity);
        BoneType.GetField("filteredLocal").SetValue(bone, initial);
        BoneType.GetField("avatarToParent").SetValue(bone, Quaternion.identity);
        BoneType.GetField("restDirection").SetValue(bone, Vector3.up);
        ((IDictionary)Field(controller, "bones"))[HumanBodyBones.Spine] = bone;
        return bone;
    }

    [MenuItem("Tools/VTuber/Run body controller regression checks")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run these checks outside Play mode.");
        GameObject root = new GameObject("Temporary tracking regression rig");
        try
        {
            HumanoidBodyController controller = root.AddComponent<HumanoidBodyController>();
            GameObject jointObject = new GameObject("Spine");
            jointObject.transform.SetParent(root.transform);
            Transform joint = jointObject.transform;
            Quaternion raised = Quaternion.Euler(50, 0, 0);
            object bone = AddBone(controller, joint, raised);

            // Reproduce Animator overwriting the joint every frame. Return-to-rest
            // must still progress from our cache, not restart from the Animator.
            float previous = 51;
            for (int i = 0; i < 120; i++)
            {
                joint.localRotation = Quaternion.Euler(90, 0, 0);
                Call(controller, "ApplyLocal", bone, Quaternion.identity, 1f / 60, false);
                float angle = Quaternion.Angle(joint.localRotation, Quaternion.identity);
                Require(angle <= previous + 0.001f, "Rest transition restarted from Animator pose.");
                previous = angle;
            }
            Require(previous < 0.1f, "Rest transition did not converge.");

            // Before the first packet, LateUpdate must not touch a bone.
            joint.localRotation = raised;
            Call(controller, "LateUpdate");
            Require(Quaternion.Angle(joint.localRotation, raised) < 0.001f, "No-input controller wrote a bone.");

            // Held local poses follow a moving parent instead of staying fixed in world space.
            controller.returnToRestWhenLost = false;
            BoneType.GetField("filteredLocal").SetValue(bone, raised);
            root.transform.rotation = Quaternion.Euler(0, 30, 0);
            Call(controller, "HoldOrResetBone", HumanBodyBones.Spine);
            Require(Quaternion.Angle(joint.localRotation, raised) < 0.001f, "Held local pose changed.");
            Require(Quaternion.Angle(joint.rotation, root.transform.rotation * raised) < 0.001f,
                "Held pose did not follow its parent.");
            root.transform.rotation = Quaternion.identity;

            // A high-confidence inferred body wrist must not drive an arm
            // when its detailed hand detection is missing (default policy).
            IDictionary boneMap = (IDictionary)Field(controller, "bones");
            boneMap[HumanBodyBones.LeftUpperArm] = bone;
            HumanoidBodyController.Point[] inferred = new HumanoidBodyController.Point[33];
            for (int i = 0; i < inferred.Length; i++)
                inferred[i] = new HumanoidBodyController.Point { x = i * 0.01f, y = 1, visibility = 1 };
            joint.localRotation = Quaternion.identity;
            Call(controller, "ApplyArm", true, inferred, null);
            Require(Quaternion.Angle(joint.localRotation, raised) < 0.001f,
                "Missing hand allowed inferred pose wrist to move the arm.");
            boneMap.Remove(HumanBodyBones.LeftUpperArm);

            // A timed-out session must release ownership as well.
            Set(controller, "current", new HumanoidBodyController.Packet());
            Set(controller, "lastPacket", Time.time - 100f);
            joint.localRotation = raised;
            Call(controller, "LateUpdate");
            Require(Quaternion.Angle(joint.localRotation, raised) < 0.001f,
                "Timed-out tracking wrote a bone.");

            // A spine-only mapping must still move the torso with fixed parent position.
            BoneType.GetField("filteredLocal").SetValue(bone, Quaternion.identity);
            ((List<HumanBodyBones>)Field(controller, "torsoBones")).Add(HumanBodyBones.Spine);
            Set(controller, "restTorsoFrame", Quaternion.identity);
            HumanoidBodyController.Point[] pose = new HumanoidBodyController.Point[33];
            for (int i = 0; i < pose.Length; i++) pose[i] = new HumanoidBodyController.Point();
            pose[11] = new HumanoidBodyController.Point { x = -0.3f, y = 1, z = -0.3f, visibility = 1 };
            pose[12] = new HumanoidBodyController.Point { x = 0.3f, y = 1, z = -0.3f, visibility = 1 };
            pose[23] = new HumanoidBodyController.Point { x = -0.2f, visibility = 1 };
            pose[24] = new HumanoidBodyController.Point { x = 0.2f, visibility = 1 };
            Vector3 fixedPosition = root.transform.position;
            for (int i = 0; i < 120; i++) Call(controller, "ApplyTorso", pose, 1f / 60);
            Require(controller.TorsoTracking, "Valid torso was rejected.");
            Require(Quaternion.Angle(joint.localRotation, Quaternion.identity) > 5, "Spine-only torso did not lean.");
            Require(root.transform.position == fixedPosition, "Torso moved its anchor.");

            // Upper-body framing: shoulders can still supply turn/side tilt without hips.
            pose[23].visibility = pose[24].visibility = 0;
            pose[12].y = 1.2f;
            Call(controller, "ApplyTorso", pose, 1f / 60);
            Require(controller.TorsoTracking, "Shoulder-only fallback was rejected.");
            controller.allowShoulderOnlyTorso = false;
            Call(controller, "ApplyTorso", pose, 1f / 60);
            Require(!controller.TorsoTracking, "Strict torso mode accepted invisible hips.");
            // Compute a separate relaxed arm target without changing bind axes.
            Transform upper = new GameObject("Upper arm").transform;
            upper.SetParent(root.transform, false);
            Transform lower = new GameObject("Forearm").transform;
            lower.SetParent(upper, false);
            lower.localPosition = Vector3.left;
            object upperBone = AddBone(controller, upper, Quaternion.identity);
            object lowerBone = AddBone(controller, lower, Quaternion.identity);
            BoneType.GetField("restDirection").SetValue(upperBone, Vector3.left);
            BoneType.GetField("restDirection").SetValue(lowerBone, Vector3.left);
            boneMap[HumanBodyBones.LeftUpperArm] = upperBone;
            boneMap[HumanBodyBones.LeftLowerArm] = lowerBone;
            Call(controller, "ConfigureRelaxedArm", true);
            Quaternion upperRest = (Quaternion)BoneType.GetField("neutralLocal").GetValue(upperBone);
            Quaternion lowerRest = (Quaternion)BoneType.GetField("neutralLocal").GetValue(lowerBone);
            for (int i = 0; i < 120; i++)
            {
                // Simulate an Animator repeatedly restoring the T-pose.
                upper.localRotation = lower.localRotation = Quaternion.identity;
                Call(controller, "ApplyLocal", upperBone, upperRest, 1f / 60, false);
                Call(controller, "ApplyLocal", lowerBone, lowerRest, 1f / 60, false);
            }
            Require(Vector3.Dot(upper.TransformDirection(Vector3.left), Vector3.down) > 0.95f,
                "Relaxed upper arm did not point down.");
            Require(Vector3.Dot(lower.TransformDirection(Vector3.left), Vector3.down) > 0.9f,
                "Relaxed forearm did not point down.");
            Require((Vector3)BoneType.GetField("restDirection").GetValue(upperBone) == Vector3.left,
                "Relaxed pose changed the retargeting bind direction.");
            Debug.Log("PASS: cached rest transition, no-input ownership, parent-relative hold, spine-only torso, shoulder fallback.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
