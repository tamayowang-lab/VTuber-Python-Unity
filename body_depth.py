"""Relative distance cue from image shoulder scale, not metric camera depth."""
from collections import deque
import math
import statistics
import time


class RelativeDepthEstimator:
    def __init__(self, calibration_frames=12):
        self.samples = deque(maxlen=calibration_frames)
        self.baseline = None
        self.filtered = 0.0
        self.previous_time = None

    def reset(self):
        self.samples.clear()
        self.baseline = None
        self.filtered = 0.0
        self.previous_time = None

    def update(self, result, width, height, timestamp=None):
        image = result.pose_landmarks
        world = result.pose_world_landmarks
        if image is None or world is None:
            return False, 0.0
        left, right = image.landmark[11], image.landmark[12]
        a, b = world.landmark[11], world.landmark[12]
        values = [left.x, left.y, right.x, right.y, a.x, a.y, a.z, b.x, b.y, b.z]
        if not all(math.isfinite(v) for v in values):
            return False, 0.0
        if min(left.visibility, right.visibility, a.visibility, b.visibility) < 0.65:
            return False, 0.0
        # Turning narrows apparent shoulders without changing distance. Reject
        # this cue when the world-space shoulder axis is substantially oblique.
        span3d = math.sqrt((a.x-b.x)**2 + (a.y-b.y)**2 + (a.z-b.z)**2)
        if span3d < 1e-6 or abs(a.z-b.z) / span3d > 0.35:
            return False, 0.0
        if not all(0 <= v <= 1 for v in [left.x, left.y, right.x, right.y]):
            return False, 0.0
        span = math.hypot((right.x-left.x) * width / height, right.y-left.y)
        if span < 0.05:
            return False, 0.0
        now = time.monotonic() if timestamp is None else timestamp
        if self.baseline is None:
            self.samples.append(span)
            if len(self.samples) < self.samples.maxlen:
                return False, 0.0
            self.baseline = statistics.median(self.samples)
            self.previous_time = now
            return True, 0.0
        # Perspective scale is inversely proportional to distance. This is a
        # relative change only; unknown focal length prevents metric recovery.
        target = max(-0.5, min(0.5, 1.0 - self.baseline / span))
        if abs(target) < 0.025:
            target = 0.0
        dt = max(0, min(0.2, now - self.previous_time))
        self.previous_time = now
        self.filtered += (target - self.filtered) * (1.0 - math.exp(-4.0 * dt))
        return True, self.filtered
