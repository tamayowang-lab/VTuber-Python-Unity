import json
from pathlib import Path
import tempfile
import unittest
from test_motion_pipeline import hand, result
from tracking.pipeline import MotionPipeline, TrackingConfig
from body_tracking import load_config


def sample(left_x, right_x, left=True, right=True):
    r = result(hand(left_x) if left else None, hand(right_x) if right else None)
    for index, x in ((15, left_x), (16, right_x)):
        r.pose_landmarks.landmark[index].x = x
        r.pose_world_landmarks.landmark[index].x = x-0.5
    return r


class ManualPoseTests(unittest.TestCase):
    def test_close_fully_visible_hands_are_not_replaced_by_a_preset(self):
        pipe = MotionPipeline()
        pipe.process(sample(0.3, 0.7), 640, 480, 0)
        for i in range(1, 40):
            x = min(0.47, 0.3+i*0.0085)
            packet = pipe.process(sample(x, 1-x), 640, 480, i*0.03)
            self.assertFalse(packet['overlapActive'])
            for key in ('leftHand', 'rightHand'):
                self.assertEqual(len(packet[key]), 21)
                self.assertEqual(packet[key][8]['visibility'], 1)

    def test_old_auto_configs_migrate_to_off_without_losing_filter_tuning(self):
        self.assertEqual(TrackingConfig(overlap_mode='auto').overlap_mode, 'off')
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'old.json'
            path.write_text(json.dumps(dict(overlap_mode='auto', overlap_enter_ratio=1.8,
                overlap_exit_seconds=0.2, min_cutoff=0.8, beta=1.5)))
            config = load_config(path)
        self.assertEqual(config.overlap_mode, 'off')
        self.assertEqual(config.min_cutoff, 0.8)
        self.assertEqual(config.beta, 1.5)

    def test_only_an_explicit_cue_withholds_fingers(self):
        config = TrackingConfig()
        pipe = MotionPipeline(config)
        pipe.process(sample(0.3, 0.7), 640, 480, 0)
        config.overlap_mode = 'force'
        packet = pipe.process(sample(0.3, 0.7), 640, 480, 0.03)
        self.assertTrue(packet['overlapActive'])
        self.assertTrue(packet['overlapManual'])
        self.assertEqual(packet['leftHand'][8]['visibility'], 0)
        config.overlap_mode = 'off'
        packet = pipe.process(sample(0.3, 0.7), 640, 480, 0.06)
        self.assertFalse(packet['overlapActive'])
        self.assertFalse(packet['overlapManual'])
        self.assertEqual(packet['leftHand'][8]['visibility'], 1)

    def test_manual_cue_held_palm_still_requires_a_body_wrist(self):
        config = TrackingConfig()
        pipe = MotionPipeline(config)
        pipe.process(sample(0.3, 0.7), 640, 480, 0)
        config.overlap_mode = 'force'
        packet = pipe.process(sample(0.3, 0.7, right=False), 640, 480, 0.2)
        self.assertEqual(packet['trackingStatus']['right'], 'overlap-held')
        self.assertEqual(packet['rightHand'][8]['visibility'], 0)
        r = sample(0.3, 0.7, right=False)
        r.pose_landmarks.landmark[16].visibility = 0
        packet = pipe.process(r, 640, 480, 0.3)
        self.assertEqual(packet['rightHand'], [])

    def test_invalid_mode_is_rejected(self):
        with self.assertRaises(ValueError): TrackingConfig(overlap_mode='not-a-mode')


if __name__ == '__main__': unittest.main()
