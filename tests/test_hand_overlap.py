import unittest
from test_motion_pipeline import hand, result
from tracking.pipeline import MotionPipeline, TrackingConfig


def sample(left_x, right_x, left=True, right=True):
    r = result(hand(left_x) if left else None, hand(right_x) if right else None)
    for index, x in ((15, left_x), (16, right_x)):
        r.pose_landmarks.landmark[index].x = x
        r.pose_world_landmarks.landmark[index].x = x-0.5
    return r


def enter(pipe):
    pipe.process(sample(0.3, 0.7), 640, 480, 0)
    packet = None
    for i in range(1, 23):
        packet = pipe.process(sample(0.3+i*0.008, 0.7-i*0.008), 640, 480, i*0.03)
    for i in range(23, 31):
        packet = pipe.process(sample(0.476, 0.524), 640, 480, i*0.03)
    return packet


class OverlapTests(unittest.TestCase):
    def test_approaching_hands_activate_with_hysteresis_and_hide_finger_uncertainty(self):
        pipe = MotionPipeline()
        packet = enter(pipe)
        self.assertTrue(packet['overlapActive'])
        for key in ('leftHand', 'rightHand'):
            self.assertEqual(len(packet[key]), 21)
            self.assertGreaterEqual(packet[key][0]['visibility'], 0.6)
            self.assertEqual(packet[key][8]['visibility'], 0)
            self.assertGreaterEqual(packet[key][5]['visibility'], 0.6)

    def test_duplicate_single_hand_does_not_create_an_interlace(self):
        pipe = MotionPipeline()
        for i in range(40):
            packet = pipe.process(result(hand(0.3), hand(0.301)), 640, 480, i/30)
            self.assertFalse(packet['overlapActive'])
            self.assertEqual(packet['rightHand'], [])

    def test_one_occluded_hand_uses_known_body_wrist_without_fake_fingers(self):
        pipe = MotionPipeline()
        self.assertTrue(enter(pipe)['overlapActive'])
        packet = pipe.process(sample(0.476, 0.524, right=False), 640, 480, 1.0)
        self.assertTrue(packet['overlapActive'])
        self.assertEqual(len(packet['rightHand']), 21)
        self.assertEqual(packet['trackingStatus']['right'], 'overlap-held')
        self.assertEqual(packet['rightHand'][8]['visibility'], 0)
        self.assertAlmostEqual(packet['rightHand'][0]['x'], -0.524)

    def test_both_hidden_hands_can_hold_only_while_body_wrists_support_overlap(self):
        pipe = MotionPipeline()
        enter(pipe)
        packet = pipe.process(sample(0.476, 0.524, left=False, right=False), 640, 480, 1)
        self.assertTrue(packet['overlapActive'])
        hidden = sample(0.476, 0.524, left=False, right=False)
        for i in (15, 16): hidden.pose_landmarks.landmark[i].visibility = 0
        packet = pipe.process(hidden, 640, 480, 1.1)
        self.assertTrue(packet['overlapActive'])
        packet = pipe.process(hidden, 640, 480, 1.5)
        self.assertFalse(packet['overlapActive'])
        self.assertEqual(packet['leftHand'], [])
        self.assertEqual(packet['rightHand'], [])

    def test_separation_recovers_identity_and_finger_tracking(self):
        pipe = MotionPipeline()
        enter(pipe)
        for i in range(1, 25):
            x = 0.476-i*0.007
            packet = pipe.process(sample(x, 1-x), 640, 480, 0.9+i*0.03)
        self.assertFalse(packet['overlapActive'])
        self.assertEqual(packet['leftHand'][8]['visibility'], 1)
        self.assertEqual(packet['rightHand'][8]['visibility'], 1)
        self.assertGreater(packet['leftHand'][0]['x'], packet['rightHand'][0]['x'])

    def test_brief_proximity_does_not_trigger_the_preset(self):
        pipe = MotionPipeline()
        pipe.process(sample(0.3, 0.7), 640, 480, 0)
        for t in (0.03, 0.06):
            self.assertFalse(pipe.process(sample(0.46, 0.54), 640, 480, t)['overlapActive'])
        self.assertFalse(pipe.process(sample(0.3, 0.7), 640, 480, 0.09)['overlapActive'])
        self.assertIsNone(pipe.overlap.pending)

    def test_force_off_and_invalid_config(self):
        config = TrackingConfig(overlap_mode='force')
        pipe = MotionPipeline(config)
        self.assertTrue(pipe.process(result(), 640, 480, 0)['overlapActive'])
        config.overlap_mode = 'off'
        self.assertFalse(pipe.process(result(), 640, 480, 1)['overlapActive'])
        with self.assertRaises(ValueError): TrackingConfig(overlap_mode='not-a-mode')
        with self.assertRaises(ValueError): TrackingConfig(overlap_enter_ratio=3)

    def test_swapped_labels_in_overlap_do_not_fill_two_tracks_with_one_candidate(self):
        pipe = MotionPipeline()
        enter(pipe)
        frame = sample(0.476, 0.524)
        frame.left_hand_landmarks, frame.right_hand_landmarks = frame.right_hand_landmarks, frame.left_hand_landmarks
        packet = pipe.process(frame, 640, 480, 0.95)
        self.assertTrue(packet['overlapActive'])
        self.assertLess(abs(packet['leftHand'][0]['x']+0.476), 0.03)
        self.assertLess(abs(packet['rightHand'][0]['x']+0.524), 0.03)
        self.assertEqual(packet['leftHand'][8]['visibility'], 0)


if __name__ == '__main__': unittest.main()
