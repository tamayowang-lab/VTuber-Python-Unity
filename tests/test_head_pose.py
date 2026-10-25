import unittest
import cv2
import numpy as np
from head_pose import HeadOrientation


def rotation(x=0, y=0, z=0):
    x, y, z = np.radians([x, y, z])
    rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return rz @ ry @ rx


def vector(matrix):
    return cv2.Rodrigues(matrix)[0]


class HeadPoseTests(unittest.TestCase):
    def test_nods_across_the_180_degree_boundary_do_not_saturate(self):
        head = HeadOrientation()
        neutral = rotation(x=180)
        np.testing.assert_allclose(head.angles(vector(neutral)), [0, 0, 0], atol=1e-5)
        for pitch in (-40, -15, 0, 15, 40):
            roll, measured, yaw = head.angles(vector(rotation(x=pitch) @ neutral))
            np.testing.assert_allclose([roll, measured, yaw], [0, pitch, 0], atol=1e-5)

    def test_turn_and_tilt_have_separate_axes(self):
        head = HeadOrientation()
        neutral = rotation(x=177, y=8, z=-3)
        head.angles(vector(neutral))
        for pitch, yaw, roll in [(10, 25, -15), (-20, -30, 12)]:
            measured = head.angles(vector(rotation(pitch, yaw, roll) @ neutral))
            np.testing.assert_allclose(measured, [roll, pitch, yaw], atol=1e-5)

    def test_recalibration_captures_new_neutral(self):
        head = HeadOrientation()
        head.angles(vector(rotation(x=180)))
        head.reset()
        np.testing.assert_allclose(head.angles(vector(rotation(165, 15, 10))), [0, 0, 0], atol=1e-5)


if __name__ == '__main__':
    unittest.main()
