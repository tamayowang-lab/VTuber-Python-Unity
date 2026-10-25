"""Camera-space head angles relative to a captured neutral orientation."""
import cv2
import numpy as np


class HeadOrientation:
    def __init__(self):
        self.neutral = None

    def reset(self):
        self.neutral = None

    def angles(self, rotation_vector):
        rotation, _ = cv2.Rodrigues(np.asarray(rotation_vector, dtype=np.float64).reshape(3, 1))
        if self.neutral is None:
            self.neutral = rotation.copy()
        # Compare rotations as matrices: raw Rodrigues components are NOT Euler
        # angles, and equivalent +/-180-degree representations must not saturate pitch.
        relative = rotation @ self.neutral.T
        pitch, yaw, roll = cv2.RQDecomp3x3(relative)[0]
        return float(roll), float(pitch), float(yaw)
