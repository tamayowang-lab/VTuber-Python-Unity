"""Explicit animation cue only; proximity cannot establish interlaced fingers."""
import numpy as np


class ManualHandPoseCue:
    def __init__(self, config):
        self.config = config
        self.active = False

    def update(self, pose, aspect):
        self.active = self.config.overlap_mode == 'force'
        wrists = [None, None]
        if not self.active:
            return False, wrists
        if pose is not None and len(pose.landmark) == 33:
            for side, index in enumerate((15, 16)):
                p = pose.landmark[index]
                if p.visibility >= self.config.min_visibility and 0 <= p.x <= 1 and 0 <= p.y <= 1:
                    wrists[side] = np.array([p.x, p.y/aspect, 0.0])
        return self.active, wrists
