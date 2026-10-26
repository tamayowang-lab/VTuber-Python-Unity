using UnityEngine;

public static class JointConstraints
{
    // Signed swing/twist decomposition avoids Euler wrap and does not clamp
    // forearm pronation or sideways deviation as if they were wrist flexion.
    public static Quaternion ClampWristBend(Quaternion target, Quaternion neutral,
        Vector3 bendAxisInParent, float minimum, float maximum)
    {
        if (bendAxisInParent.sqrMagnitude < 1e-8f) return target;
        Vector3 axis = bendAxisInParent.normalized;
        Quaternion offset = target * Quaternion.Inverse(neutral);
        if (offset.w < 0) offset = new Quaternion(-offset.x, -offset.y, -offset.z, -offset.w);
        float projected = Vector3.Dot(new Vector3(offset.x, offset.y, offset.z), axis);
        if (projected*projected + offset.w*offset.w < 1e-10f) return target;
        float angle = Mathf.DeltaAngle(0, 2*Mathf.Atan2(projected, offset.w)*Mathf.Rad2Deg);
        float low = Mathf.Clamp(Mathf.Min(minimum, maximum), -90, 90);
        float high = Mathf.Clamp(Mathf.Max(minimum, maximum), -90, 90);
        Quaternion twist = Quaternion.AngleAxis(angle, axis);
        Quaternion remainder = Quaternion.Inverse(twist)*offset;
        return Quaternion.AngleAxis(Mathf.Clamp(angle, low, high), axis)*remainder*neutral;
    }
}
