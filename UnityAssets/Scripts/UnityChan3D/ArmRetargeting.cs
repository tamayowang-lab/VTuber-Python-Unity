using UnityEngine;

// Shared analytic solver for measured AND hand-inferred wrists. No package,
// iterative scene rig or per-frame allocation; never writes joint positions.
public static class ArmRetargeting
{
    public struct Limits
    {
        public bool enabled, left;
        public Vector3 center, right;
        public float clearance, minimumBend, maximumBend;
    }

    public static Quaternion AimFrame(Vector3 direction, Vector3 planeNormal)
    {
        if (direction.sqrMagnitude < 1e-10f) return Quaternion.identity;
        Vector3 normal = Vector3.ProjectOnPlane(planeNormal, direction.normalized);
        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.ProjectOnPlane(Vector3.forward, direction.normalized);
        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.ProjectOnPlane(Vector3.up, direction.normalized);
        return Quaternion.LookRotation(direction.normalized, normal.normalized);
    }

    public static bool Solve(Vector3 shoulder, Vector3 target, float upper, float lower,
        Vector3 hint, Vector3 pole, Limits limits, out Vector3 elbow, out Vector3 wrist)
    {
        elbow = wrist = shoulder;
        if (upper < 1e-5f || lower < 1e-5f) return false;
        float total = upper + lower;
        float minBend = Mathf.Clamp(limits.minimumBend, 1, 80);
        float maxBend = Mathf.Clamp(limits.maximumBend, minBend + 1, 170);
        float nearest = Mathf.Sqrt(upper*upper + lower*lower + 2*upper*lower*Mathf.Cos(maxBend*Mathf.Deg2Rad));
        float farthest = Mathf.Sqrt(upper*upper + lower*lower + 2*upper*lower*Mathf.Cos(minBend*Mathf.Deg2Rad));
        Vector3 outward = limits.right.normalized * (limits.left ? -1 : 1);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            // Preserve the measured wrist whenever feasible. Only an impossible
            // clearance target is nudged outward, with bounded retries.
            Vector3 delta = target - shoulder + outward*(attempt*total*0.1f);
            if (delta.sqrMagnitude < 1e-10f) delta = pole*total*0.5f;
            Vector3 axis = delta.normalized;
            float distance = Mathf.Clamp(delta.magnitude, nearest, farthest);
            wrist = shoulder + axis*distance;
            float along = (upper*upper - lower*lower + distance*distance)/(2*distance);
            float radius = Mathf.Sqrt(Mathf.Max(0, upper*upper-along*along));
            Vector3 center = shoulder + axis*along;
            Vector3 bend = Vector3.ProjectOnPlane(hint-center, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(pole, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, axis);
            bend.Normalize();
            if (limits.enabled && outward.sqrMagnitude > 0)
            {
                Vector3 projected = Vector3.ProjectOnPlane(outward, axis);
                float available = projected.magnitude*radius;
                float required = limits.clearance-Vector3.Dot(center-limits.center, outward);
                if (required > available + 1e-6f) continue;
                if (available > 1e-8f && Vector3.Dot(bend, projected.normalized)*available < required)
                {
                    Vector3 side = projected.normalized;
                    float dot = Mathf.Clamp(required/available, -1, 1);
                    Vector3 tangent = Vector3.ProjectOnPlane(bend, side);
                    if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(axis, side);
                    bend = side*dot + tangent.normalized*Mathf.Sqrt(Mathf.Max(0, 1-dot*dot));
                }
            }
            elbow = center + bend*radius;
            return true;
        }
        return false; // Caller holds/rests rather than emitting an unsafe pose.
    }
}
