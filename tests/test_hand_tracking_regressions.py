import json
from pathlib import Path
import tempfile
import unittest
from test_motion_pipeline import hand, result
from tracking.pipeline import MotionPipeline
from body_tracking import load_config


class HandRegressionTests(unittest.TestCase):
    def test_close_visible_hands_are_never_replaced_by_a_preset(self):
        pipe = MotionPipeline()
        for i in range(40):
            x = min(0.47, 0.3+i*0.0085)
            sample = result(hand(x), hand(1-x))
            for index, wrist in ((15, x), (16, 1-x)):
                sample.pose_landmarks.landmark[index].x = wrist
                sample.pose_world_landmarks.landmark[index].x = wrist-0.5
            packet = pipe.process(sample, 640, 480, i*0.03)
            self.assertNotIn('overlapActive', packet)
            for key in ('leftHand', 'rightHand'):
                self.assertEqual(len(packet[key]), 21)
                self.assertEqual(packet[key][8]['visibility'], 1)

    def test_removed_settings_are_ignored_without_losing_filter_tuning(self):
        for old_mode in ('auto', 'force'):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory)/'old.json'
                path.write_text(json.dumps(dict(overlap_mode=old_mode, overlap_enter_ratio=1.8,
                    overlap_exit_seconds=0.2, min_cutoff=0.8, beta=1.5)))
                config = load_config(path)
            self.assertFalse(hasattr(config, 'overlap_mode'))
            self.assertEqual(config.min_cutoff, 0.8)
            self.assertEqual(config.beta, 1.5)


if __name__ == '__main__': unittest.main()
