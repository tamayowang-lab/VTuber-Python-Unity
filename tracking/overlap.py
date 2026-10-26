"""Cheap overlap hysteresis, not an interlaced-finger reconstruction model."""
import numpy as np


class HandOverlap:
    def __init__(self, config):
        self.config = config
        self.active = False
        self.entered = None
        self.pending = self.separating = None
        self.last_evidence = None

    @staticmethod
    def wrists(pose, aspect, visibility):
        if pose is None or len(pose.landmark) != 33:
            return [None, None]
        wrists = []
        for i in (15, 16):
            p = pose.landmark[i]
            wrists.append(np.array([p.x, p.y/aspect, 0.0])
                          if p.visibility >= visibility and 0 <= p.x <= 1 and 0 <= p.y <= 1 else None)
        return wrists

    def update(self, candidates, tracks, pose, aspect, now):
        was_active = self.active
        wrists = self.wrists(pose, aspect, self.config.min_visibility)
        fresh = all(t.time is not None and now-t.time <= self.config.identity_memory for t in tracks)
        sizes = [max(np.linalg.norm(t.raw[9]-t.raw[0]), np.linalg.norm(t.raw[5]-t.raw[17]))
                 for t in tracks if t.raw is not None]
        scale = float(np.mean(sizes)) if len(sizes) == 2 else 0
        anchored = all(w is not None for w in wrists)
        close = anchored and scale > 1e-5 and np.linalg.norm(wrists[0]-wrists[1]) < self.config.overlap_enter_ratio*scale
        separate = anchored and scale > 1e-5 and np.linalg.norm(wrists[0]-wrists[1]) > self.config.overlap_exit_ratio*scale
        # Require two previously established identities: one hallucinated pair
        # of duplicate skeletons must not activate an approximation for two hands.
        enough_evidence = anchored and (fresh or self.active) and (len(candidates) > 0 or (self.active and close))
        if enough_evidence:
            self.last_evidence = now
        if self.config.overlap_mode == 'off':
            self.active = False
        elif self.config.overlap_mode == 'force':
            self.active = True
        elif not self.active:
            if close and fresh and len(candidates) > 0:
                if self.pending is None: self.pending = now
                if now-self.pending >= self.config.overlap_enter_seconds:
                    self.active = True
            else:
                self.pending = None
        else:
            lost = self.last_evidence is None or now-self.last_evidence > self.config.overlap_lost_seconds
            if separate:
                if self.separating is None: self.separating = now
            else:
                self.separating = None
            if lost or (self.separating is not None and now-self.separating >= self.config.overlap_exit_seconds):
                self.active = False
        if self.active and self.entered is None:
            self.entered = now
        if not self.active:
            self.entered = None
            self.separating = None
            if was_active or self.config.overlap_mode != 'auto':
                self.pending = None
        return self.active, wrists
