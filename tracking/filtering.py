"""Vectorized One Euro filter (Casiez et al.), with monotonic timestamps."""
import numpy as np


class OneEuroFilter:
    def __init__(self, min_cutoff=1.2, beta=0.6, derivative_cutoff=1.0, reset_after=0.5):
        if min_cutoff <= 0 or beta < 0 or derivative_cutoff <= 0 or reset_after <= 0:
            raise ValueError('Cutoffs/reset_after must be positive and beta nonnegative')
        self.min_cutoff, self.beta = min_cutoff, beta
        self.derivative_cutoff, self.reset_after = derivative_cutoff, reset_after
        self.time = self.raw = self.value = self.derivative = None

    @staticmethod
    def alpha(cutoff, dt):
        return 1.0 / (1.0 + 1.0 / (2.0 * np.pi * cutoff * dt))

    def update(self, value, timestamp):
        value = np.asarray(value, dtype=np.float64)
        if not np.isfinite(value).all() or not np.isfinite(timestamp):
            raise ValueError('Filter input and timestamp must be finite')
        if self.time is not None and timestamp <= self.time:
            return self.value.copy()  # Do not corrupt state on duplicate/reordered samples.
        if self.time is None or value.shape != self.value.shape or timestamp-self.time > self.reset_after:
            self.time, self.raw, self.value = timestamp, value.copy(), value.copy()
            self.derivative = np.zeros_like(value)
            return self.value.copy()
        dt = timestamp-self.time
        speed = (value-self.raw)/dt
        a = self.alpha(self.derivative_cutoff, dt)
        self.derivative += a*(speed-self.derivative)
        cutoff = self.min_cutoff + self.beta*np.abs(self.derivative)
        self.value += self.alpha(cutoff, dt)*(value-self.value)
        self.raw, self.time = value.copy(), timestamp
        return self.value.copy()
