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

    static bool SolveArm(Vector3 shoulder, Vector3 target, float upper, float lower, Vector3 pole,
        out Vector3 elbow, out Vector3 wrist)
    {
        return ArmRetargeting.Solve(shoulder, target, upper, lower, shoulder+pole, pole,
            new ArmRetargeting.Limits { minimumBend = 15, maximumBend = 150 }, out elbow, out wrist);
    }
    static bool SolveLimitedArm(Vector3 shoulder, Vector3 hint, Vector3 target, Vector3 center,
        Vector3 right, bool left, float clearance, float upper, float lower, out Vector3 elbow, out Vector3 wrist)
    {
        return ArmRetargeting.Solve(shoulder, target, upper, lower, hint,
            right*(left ? -1 : 1)+Vector3.down,
            new ArmRetargeting.Limits { enabled = true, left = left, center = center, right = right,
                clearance = clearance, minimumBend = 15, maximumBend = 150 }, out elbow, out wrist);
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
            controller.requireHandForArmTracking = true;
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
            Require(Vector3.Dot(upper.TransformDirection(Vector3.left), Vector3.down) > 0.93f,
                "Relaxed upper arm did not point down.");
            Require(Vector3.Dot(lower.TransformDirection(Vector3.left), Vector3.down) > 0.88f,
                "Relaxed forearm did not point down.");
            Require((Vector3)BoneType.GetField("restDirection").GetValue(upperBone) == Vector3.left,
                "Relaxed pose changed the retargeting bind direction.");
            // Correct proportions: scale a shoulder-relative wrist once, not each
            // human segment independently. Bind rotations must not change the target.
            Type geometryType = typeof(HumanoidBodyController).GetNestedType("ArmGeometry", BindingFlags.NonPublic);
            object geometry = Activator.CreateInstance(geometryType, true);
            geometryType.GetField("upperLength").SetValue(geometry, 0.3f);
            geometryType.GetField("lowerLength").SetValue(geometry, 0.25f);
            geometryType.GetField("lastPole").SetValue(geometry, new Vector3(-0.5f, -1, 0.5f));
            ((IDictionary)Field(controller, "armGeometry"))[true] = geometry;
            controller.limitElbowInward = false;
            Vector3 observedUpper = new Vector3(-0.2f, -0.2f, 0.1f);
            Vector3 observedLower = new Vector3(-0.1f, 0.1f, 0.2f);
            Vector3 expectedWrist = root.transform.InverseTransformPoint(upper.position)
                + (observedUpper+observedLower)*(0.55f/(observedUpper.magnitude+observedLower.magnitude));
            Require((bool)Call(controller, "DriveTrackedArm", true, observedUpper, observedLower),
                "Measured arm failed retargeting.");
            Vector3[] wristTargets = (Vector3[])Field(controller, "debugWrist");
            Require((wristTargets[0]-expectedWrist).magnitude < 0.001f,
                "Retargeting changed measured wrist trajectory due to bone proportions.");
            // Collapsed finger geometry must be rejected independently.
            HumanoidBodyController.Point[] hand = new HumanoidBodyController.Point[21];
            for (int i = 0; i < hand.Length; i++)
                hand[i] = new HumanoidBodyController.Point { x = (i % 4) * 0.02f, y = i * 0.02f, visibility = 1 };
            Require((bool)Call(controller, "FingerReliable", hand, 1, 0.15f), "Valid finger was rejected.");
            hand[6] = hand[5];
            Require(!(bool)Call(controller, "FingerReliable", hand, 1, 0.15f), "Collapsed finger was trusted.");
            Require((bool)Call(controller, "FingerReliable", hand, 2, 0.15f), "One bad finger rejected another.");

            // Whole-avatar depth must be bounded and respect explicit mode choice.
            Set(controller, "rootRestPosition", root.transform.localPosition);
            Set(controller, "rootForwardInParent", Vector3.forward);
            Set(controller, "current", new HumanoidBodyController.Packet { depthValid = true, depthOffset = 0.5f });
            controller.depthMotion = HumanoidBodyController.DepthMotionMode.WholeAvatar;
            for (int i = 0; i < 180; i++) Call(controller, "ApplyDepthMotion", true, 1f / 60);
            Require(root.transform.localPosition.z > 0.1f, "Whole-avatar depth did not move forward.");
            Require(root.transform.localPosition.z <= controller.maximumDepthTranslation + 0.001f,
                "Whole-avatar depth exceeded its limit.");
            controller.depthMotion = HumanoidBodyController.DepthMotionMode.UpperBodyLean;
            for (int i = 0; i < 240; i++) Call(controller, "ApplyDepthMotion", true, 1f / 60);
            Require(Mathf.Abs(root.transform.localPosition.z) < 0.001f,
                "Fixed-hip mode retained root translation.");
            // Constrained hand-only IK must preserve lengths even for targets
            // far beyond reach, and must avoid a locked straight elbow.
            foreach (Vector3 target in new[] { new Vector3(-10, 4, 0.2f),
                new Vector3(-0.1f, -0.5f, 0.1f), new Vector3(-0.01f, 0, 0.01f) })
            {
                Vector3 elbow, wrist;
                Require(SolveArm(Vector3.zero, target,
                    0.3f, 0.25f, new Vector3(-0.5f, -1, 0.5f), out elbow, out wrist), "IK rejected valid lengths.");
                Require(Mathf.Abs(elbow.magnitude - 0.3f) < 0.001f, "IK stretched upper arm.");
                Require(Mathf.Abs((wrist - elbow).magnitude - 0.25f) < 0.001f, "IK stretched forearm.");
                Require(wrist.magnitude <= 0.55f, "IK exceeded safe reach.");
                float bend = Vector3.Angle(elbow, wrist-elbow);
                Require(bend >= 14.9f && bend <= 150.1f, "IK violated elbow bend limits.");
            }
            Vector3 rejectedElbow, rejectedWrist;
            Require(!SolveArm(Vector3.zero, Vector3.one,
                0, 0.25f, Vector3.down, out rejectedElbow, out rejectedWrist), "IK accepted a zero-length bone.");
            // Keep elbows outside a torso-relative side boundary, preserving lengths.
            Vector3 leftShoulder = new Vector3(-0.2f, 1, 0);
            Vector3 badElbow = new Vector3(0, 0.8f, 0);
            Vector3 handTarget = new Vector3(0, 0.6f, 0.2f);
            Vector3 safeElbow, safeWrist;
            Require(SolveLimitedArm(leftShoulder, badElbow, handTarget,
                new Vector3(0, 1, 0), Vector3.right, true, 0.14f, 0.3f, 0.25f,
                out safeElbow, out safeWrist), "Elbow constraint rejected a valid rig.");
            Require(safeElbow.x <= -0.14f + 0.001f, "Left elbow remained in chest center.");
            Require(Mathf.Abs((safeElbow - leftShoulder).magnitude - 0.3f) < 0.001f,
                "Elbow constraint stretched upper arm.");
            Require(Mathf.Abs((safeWrist - safeElbow).magnitude - 0.25f) < 0.001f,
                "Elbow constraint stretched forearm.");
            Vector3 mirrorElbow, mirrorWrist;
            SolveLimitedArm(new Vector3(0.2f, 1, 0), badElbow, handTarget,
                new Vector3(0, 1, 0), Vector3.right, false, 0.14f, 0.3f, 0.25f,
                out mirrorElbow, out mirrorWrist);
            Require(mirrorElbow.x >= 0.14f - 0.001f, "Right elbow remained in chest center.");
            Quaternion torsoTurn = Quaternion.Euler(0, 35, 15);
            Vector3 turnedElbow, turnedWrist;
            SolveLimitedArm(torsoTurn * leftShoulder, torsoTurn * badElbow,
                torsoTurn * handTarget, torsoTurn * new Vector3(0, 1, 0), torsoTurn * Vector3.right,
                true, 0.14f, 0.3f, 0.25f, out turnedElbow, out turnedWrist);
            Require((turnedElbow - torsoTurn * safeElbow).magnitude < 0.001f,
                "Elbow limit did not follow torso rotation.");
            BoneType.GetField("finger").SetValue(bone, true);
            BoneType.GetField("filteredLocal").SetValue(bone, Quaternion.identity);
            for (int i = 0; i < 120; i++)
                Call(controller, "ApplyLocal", bone, Quaternion.Euler(170, 0, 0), 1f/60, true);
            Require(Quaternion.Angle(joint.localRotation, Quaternion.identity)
                <= controller.maximumFingerJointRotation+0.01f, "Finger exceeded its bind-relative safety cone.");
            Quaternion wristPositive = JointConstraints.ClampWristBend(Quaternion.AngleAxis(75, Vector3.right),
                Quaternion.identity, Vector3.right, -35, 45);
            Require(Quaternion.Angle(wristPositive, Quaternion.AngleAxis(45, Vector3.right)) < 0.01f,
                "Positive wrist bend exceeded its limit.");
            Quaternion wristNegative = JointConstraints.ClampWristBend(Quaternion.AngleAxis(-70, Vector3.right),
                Quaternion.identity, Vector3.right, -35, 45);
            Require(Quaternion.Angle(wristNegative, Quaternion.AngleAxis(-35, Vector3.right)) < 0.01f,
                "Negative wrist bend exceeded its limit.");
            Quaternion pureTwist = Quaternion.AngleAxis(65, Vector3.up);
            Require(Quaternion.Angle(JointConstraints.ClampWristBend(pureTwist, Quaternion.identity,
                Vector3.right, -35, 45), pureTwist) < 0.01f, "Wrist bend limit changed a pure forearm twist.");
            Quaternion parentFrame = Quaternion.Euler(20, 70, -15);
            Quaternion neutralWrist = Quaternion.Euler(-30, 10, 45);
            Quaternion transformed = parentFrame*Quaternion.AngleAxis(75, Vector3.right)*Quaternion.Inverse(parentFrame)*neutralWrist;
            Quaternion clamped = JointConstraints.ClampWristBend(transformed, neutralWrist,
                parentFrame*Vector3.right, -35, 45);
            Quaternion expectedWristBend = parentFrame*Quaternion.AngleAxis(45, Vector3.right)*Quaternion.Inverse(parentFrame)*neutralWrist;
            Require(Quaternion.Angle(clamped, expectedWristBend) < 0.01f, "Wrist limit assumed fixed bone axes.");
            Debug.Log("PASS: cached rest transition, no-input ownership, parent-relative hold, spine-only torso, shoulder fallback.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
