"""Camera-space head angles with stable multi-frame neutral calibration."""
import cv2
import numpy as np


class HeadOrientation:
    def __init__(self, calibration_frames=12, stability_degrees=8):
        if calibration_frames < 1 or stability_degrees <= 0:
            raise ValueError('Calibration frames/stability must be positive')
        self.calibration_frames = calibration_frames
        self.stability_degrees = stability_degrees
        self.reset()

    def reset(self):
        self.neutral = None
        self.samples = []

    def angles(self, rotation_vector):
        rotation, _ = cv2.Rodrigues(np.asarray(rotation_vector, dtype=np.float64).reshape(3, 1))
        if not np.isfinite(rotation).all():
            raise ValueError('Head rotation must be finite')
        if self.neutral is None:
            if self.samples:
                relative = rotation @ self.samples[0].T
                angle = np.degrees(np.arccos(np.clip((np.trace(relative)-1)/2, -1, 1)))
                if angle > self.stability_degrees:
                    self.samples.clear()
            self.samples.append(rotation)
            if len(self.samples) < self.calibration_frames:
                return 0.0, 0.0, 0.0
            # Project the mean onto SO(3); averaging Euler angles would fail at +/-180.
            u, _, vt = np.linalg.svd(np.mean(self.samples, axis=0))
            correction = np.diag([1, 1, np.linalg.det(u@vt)])
            self.neutral = u@correction@vt
            self.samples.clear()
        relative = rotation @ self.neutral.T
        pitch, yaw, roll = cv2.RQDecomp3x3(relative)[0]
        return float(roll), float(pitch), float(yaw)
