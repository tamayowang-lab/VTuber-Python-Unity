"""Synthetic temporal-stage overhead only; excludes camera/inference/rendering."""
import sys
from pathlib import Path
import time
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from test_motion_pipeline import hand, result
from tracking.pipeline import MotionPipeline

if __name__ == '__main__':
    pipeline = MotionPipeline()
    times = []
    for i in range(400):
        sample = result(hand(0.3), hand(0.7))
        start = time.perf_counter()
        pipeline.process(sample, 640, 480, i/60)
        times.append((time.perf_counter()-start)*1000)
    print('Temporal stage: mean %.3f ms, p95 %.3f ms (not end-to-end FPS)' %
          (np.mean(times), np.percentile(times, 95)))
