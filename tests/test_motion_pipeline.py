import unittest
from types import SimpleNamespace as NS
import numpy as np
from tracking.filtering import OneEuroFilter
from tracking.pipeline import MotionPipeline, TrackingConfig


def hand(x, y=0.5):
    # Palm bases and four nondegenerate joints per finger, normalized image units.
    pts = [(x, y, 0)]
    for finger in range(5):
        for segment in range(4):
            pts.append((x+(finger-2)*0.015, y-0.025*(segment+1), -0.004*segment))
    return NS(landmark=[NS(x=a, y=b, z=c, visibility=1) for a, b, c in pts])


def result(left=None, right=None):
    image = [NS(x=0.5, y=0.6, z=0, visibility=1) for _ in range(33)]
    for i, x, y in [(11, 0.3, 0.3), (12, 0.7, 0.3), (13, 0.3, 0.4),
                    (14, 0.7, 0.4), (15, 0.3, 0.5), (16, 0.7, 0.5)]:
        image[i] = NS(x=x, y=y, z=0, visibility=1)
    world = [NS(x=p.x-0.5, y=p.y, z=0, visibility=1) for p in image]
    return NS(pose_landmarks=NS(landmark=image), pose_world_landmarks=NS(landmark=world),
              left_hand_landmarks=left, right_hand_landmarks=right)


class EuroTests(unittest.TestCase):
    def test_stationary_jitter_is_reduced_and_fast_motion_is_responsive(self):
        static = OneEuroFilter(min_cutoff=1, beta=0)
        adaptive = OneEuroFilter(min_cutoff=1, beta=3)
        rng = np.random.default_rng(1)
        noise = rng.normal(0, 0.005, 120)
        filtered = [static.update(v, i/60).item() for i, v in enumerate(noise)]
        self.assertLess(np.std(filtered[30:]), np.std(noise[30:])*0.5)
        static = OneEuroFilter(1, 0)
        for i in range(30):
            static.update(0, i/60); adaptive.update(0, i/60)
        slow = static.update(1, 0.5).item()
        fast = adaptive.update(1, 0.5).item()
        self.assertGreater(fast, slow*3)

    def test_variable_fps_duplicates_and_gap(self):
        f = OneEuroFilter()
        for t in [0, 0.01, 0.05, 0.2]:
            self.assertTrue(np.isfinite(f.update([t, -t], t)).all())
        before = f.value.copy()
        np.testing.assert_equal(f.update([99, 99], 0.1), before)
        np.testing.assert_equal(f.update([3, 4], 1.0), [3, 4])
        with self.assertRaises(ValueError): f.update([np.nan, 1], 2)


class TemporalTests(unittest.TestCase):
    def test_only_left_and_only_right_even_with_wrong_label(self):
        for side in (0, 1):
            pipe = MotionPipeline()
            x = 0.3 if side == 0 else 0.7
            sample = result(None, hand(x)) if side == 0 else result(hand(x), None)
            packet = pipe.process(sample, 640, 480, 1)
            self.assertEqual(len(packet['leftHand' if side == 0 else 'rightHand']), 21)
            self.assertEqual(packet['rightHand' if side == 0 else 'leftHand'], [])

    def test_temporary_label_swap_does_not_switch_identity(self):
        pipe = MotionPipeline()
        first = pipe.process(result(hand(0.3), hand(0.7)), 640, 480, 0)
        swapped = pipe.process(result(hand(0.7), hand(0.3)), 640, 480, 1/30)
        for key in ('leftHand', 'rightHand'):
            self.assertAlmostEqual(first[key][0]['x'], swapped[key][0]['x'])

    def test_history_retains_side_when_wrists_temporarily_unreliable(self):
        pipe = MotionPipeline()
        pipe.process(result(hand(0.3), hand(0.7)), 640, 480, 0)
        sample = result(hand(0.7), hand(0.3))
        for i in (11, 12, 13, 14, 15, 16): sample.pose_landmarks.landmark[i].visibility = 0
        packet = pipe.process(sample, 640, 480, 0.03)
        self.assertAlmostEqual(packet['leftHand'][0]['x'], -0.3)
        self.assertAlmostEqual(packet['rightHand'][0]['x'], -0.7)

    def test_duplicate_is_not_assigned_twice(self):
        pipe = MotionPipeline()
        packet = pipe.process(result(hand(0.3), hand(0.301)), 640, 480, 0)
        self.assertEqual(len(packet['leftHand']), 21)
        self.assertEqual(packet['rightHand'], [])

    def test_loss_prediction_is_bounded_and_recovery_smooth(self):
        pipe = MotionPipeline(TrackingConfig(prediction_seconds=0.08))
        pipe.process(result(hand(0.3)), 640, 480, 0)
        pipe.process(result(hand(0.31)), 640, 480, 0.03)
        lost = pipe.process(result(), 640, 480, 0.06)
        self.assertEqual(lost['trackingStatus']['left'], 'predicted')
        self.assertLess(abs(lost['leftHand'][0]['x']+0.31), 0.03)
        expired = pipe.process(result(), 640, 480, 0.2)
        self.assertEqual(expired['leftHand'], [])
        recovered = pipe.process(result(hand(0.32)), 640, 480, 0.23)
        self.assertEqual(len(recovered['leftHand']), 21)

    def test_single_finger_jump_does_not_disable_palm_or_other_fingers(self):
        pipe = MotionPipeline()
        pipe.process(result(hand(0.3)), 640, 480, 0)
        h = hand(0.3); h.landmark[8].x += 0.8
        packet = pipe.process(result(h), 640, 480, 0.03)
        self.assertEqual(packet['leftHand'][0]['visibility'], 1)
        self.assertEqual(packet['leftHand'][8]['visibility'], 0)
        self.assertEqual(packet['leftHand'][5]['visibility'], 1)
        self.assertEqual(packet['leftHand'][9]['visibility'], 1)
        self.assertEqual(packet['leftHand'][17]['visibility'], 1)
        self.assertEqual(packet['leftHand'][12]['visibility'], 1)
        self.assertEqual(packet['trackingStatus']['left'], 'partial')

    def test_rejected_finger_recovers_without_restarting_the_whole_track(self):
        pipe = MotionPipeline()
        invalid = hand(0.3); invalid.landmark[8].x += 0.8
        packet = pipe.process(result(invalid), 640, 480, 0)
        self.assertEqual(packet['leftHand'][8]['visibility'], 0)
        packet = pipe.process(result(hand(0.3)), 640, 480, 0.03)
        self.assertEqual(packet['leftHand'][8]['visibility'], 1)
        self.assertLess(abs(packet['leftHand'][8]['x']+hand(0.3).landmark[8].x), 0.01)

    def test_nonfinite_fingertip_is_isolated_not_transmitted(self):
        import json
        pipe = MotionPipeline()
        invalid = hand(0.3); invalid.landmark[8].x = float('nan')
        packet = pipe.process(result(invalid), 640, 480, 0)
        self.assertEqual(packet['leftHand'][0]['visibility'], 1)
        self.assertEqual(packet['leftHand'][8]['visibility'], 0)
        json.dumps(packet, allow_nan=False)

    def test_world_jump_rejected_then_consistent_reacquisition(self):
        pipe = MotionPipeline()
        pipe.process(result(), 640, 480, 0)
        for i in range(1, 4):
            sample = result(); sample.pose_world_landmarks.landmark[15].x += 0.5
            packet = pipe.process(sample, 640, 480, i/60)
            self.assertEqual(packet['pose'][15]['visibility'] > 0, i == 3)

    def test_rollover_of_palm_normal_does_not_relabel_the_hand(self):
        pipe = MotionPipeline()
        for frame in range(20):
            h = hand(0.3)
            for p in h.landmark:
                y, z = p.y-0.5, p.z
                angle = frame*np.pi/19
                p.y = 0.5+y*np.cos(angle)-z*np.sin(angle)
                p.z = y*np.sin(angle)+z*np.cos(angle)
            packet = pipe.process(result(h), 640, 480, frame/30)
            self.assertEqual(len(packet['leftHand']), 21)
            self.assertEqual(packet['rightHand'], [])

    def test_low_confidence_missing_and_implausible_are_distinct(self):
        low = result(); low.pose_world_landmarks.landmark[15].visibility = 0.1
        self.assertEqual(MotionPipeline().process(low, 640, 480, 0)['trackingStatus']['pose'], 'low-confidence')
        bad = result(); bad.pose_world_landmarks.landmark[15].x += 2
        self.assertEqual(MotionPipeline().process(bad, 640, 480, 0)['trackingStatus']['pose'], 'implausible')
        missing = result(); missing.pose_world_landmarks = None
        self.assertEqual(MotionPipeline().process(missing, 640, 480, 0)['trackingStatus']['pose'], 'missing')

    def test_stationary_two_hand_output_reduces_jitter(self):
        pipe = MotionPipeline()
        rng = np.random.default_rng(5)
        raw, left, right = [], [], []
        for i in range(120):
            noise = rng.normal(0, 0.001)
            packet = pipe.process(result(hand(0.3+noise), hand(0.7+noise)), 640, 480, i/60)
            raw.append(noise)
            left.append(packet['leftHand'][0]['x'])
            right.append(packet['rightHand'][0]['x'])
        self.assertLess(np.std(left[30:]), np.std(raw[30:])*0.6)
        self.assertLess(np.std(right[30:]), np.std(raw[30:])*0.6)

    def test_single_frame_palm_flip_is_not_sent_as_observed_rotation(self):
        pipe = MotionPipeline()
        pipe.process(result(hand(0.3)), 640, 480, 0)
        flipped = hand(0.3)
        for p in flipped.landmark:
            p.y = 1-p.y; p.z = -p.z
        packet = pipe.process(result(flipped), 640, 480, 0.03)
        self.assertEqual(packet['trackingStatus']['left'], 'predicted')
        self.assertLess(abs(packet['leftHand'][9]['y']+hand(0.3).landmark[9].y/(640/480)), 0.01)

    def test_extreme_finite_coordinates_cannot_poison_the_packet(self):
        sample = result()
        sample.pose_world_landmarks.landmark[0].x = 1e30
        sample.pose_landmarks.landmark[0].x = 1e30
        packet = MotionPipeline().process(sample, 640, 480, 0)
        self.assertEqual(packet['pose'][0]['visibility'], 0)
        self.assertEqual(packet['pose'][0]['x'], 0)
        self.assertEqual(packet['imagePose'][0]['visibility'], 0)
        self.assertEqual(packet['imagePose'][0]['x'], 0)

    def test_low_confidence_image_anchor_does_not_pollute_filter(self):
        pipe = MotionPipeline()
        first = pipe.process(result(), 640, 480, 0)
        sample = result()
        sample.pose_landmarks.landmark[11].x = 0.9
        sample.pose_landmarks.landmark[11].visibility = 0.1
        packet = pipe.process(sample, 640, 480, 0.03)
        self.assertEqual(packet['imagePose'][11]['visibility'], 0)
        self.assertEqual(packet['imagePose'][11]['x'], first['imagePose'][11]['x'])

    def test_wire_coordinates_and_reordered_time(self):
        pipe = MotionPipeline()
        packet = pipe.process(result(hand(0.3)), 640, 480, 1)
        wrist = packet['leftHand'][0]
        self.assertAlmostEqual(wrist['x'], -0.3)
        self.assertAlmostEqual(wrist['y'], -0.5/(640/480))
        self.assertEqual(pipe.process(result(hand(0.7)), 640, 480, 0.9), packet)


if __name__ == '__main__': unittest.main()
