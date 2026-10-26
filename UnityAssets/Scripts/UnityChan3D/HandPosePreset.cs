using System;
using UnityEngine;

// An intentionally approximate hidden-finger pose, not reconstructed landmarks.
// Values are proximal/intermediate/distal flexion in the calibrated palm basis.
[Serializable]
public class HandPosePreset
{
    public Vector3 thumb = new Vector3(12, 20, 8);
    public Vector3 index = new Vector3(30, 40, 20);
    public Vector3 middle = new Vector3(35, 45, 25);
    public Vector3 ring = new Vector3(40, 45, 30);
    public Vector3 little = new Vector3(40, 50, 30);

    public float Curl(int finger, int segment)
    {
        Vector3 angles = finger == 0 ? thumb : finger == 1 ? index
            : finger == 2 ? middle : finger == 3 ? ring : little;
        return Mathf.Clamp(segment == 0 ? angles.x : segment == 1 ? angles.y : angles.z, 0, 100);
    }
}
