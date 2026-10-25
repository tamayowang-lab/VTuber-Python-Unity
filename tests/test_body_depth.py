import copy
import unittest
from types import SimpleNamespace as NS
from body_depth import RelativeDepthEstimator


def person(span=0.3):
    image = [NS(x=0.5, y=0.5, z=0, visibility=1) for _ in range(33)]
    world = copy.deepcopy(image)
    image[11].x, image[12].x = 0.5-span/2, 0.5+span/2
    world[11].x, world[12].x = -0.2, 0.2
    return NS(pose_landmarks=NS(landmark=image), pose_world_landmarks=NS(landmark=world))


class DepthTests(unittest.TestCase):
    def calibrated(self):
        estimator = RelativeDepthEstimator(calibration_frames=3)
        for i in range(3):
            valid, offset = estimator.update(person(), 640, 480, i/30)
        self.assertTrue(valid)
        self.assertEqual(offset, 0)
        return estimator

    def test_toward_and_away_have_opposite_signs(self):
        for span, sign in [(0.4, 1), (0.2, -1)]:
            estimator = self.calibrated()
            for i in range(3, 90):
                valid, offset = estimator.update(person(span), 640, 480, i/30)
            self.assertTrue(valid)
            self.assertGreater(sign * offset, 0.1)
            self.assertLessEqual(abs(offset), 0.5)

    def test_turning_and_missing_shoulders_do_not_move_body(self):
        estimator = self.calibrated()
        result = person(0.2)
        result.pose_world_landmarks.landmark[12].z = 0.4
        self.assertEqual(estimator.update(result, 640, 480, 1), (False, 0.0))
        result = person()
        result.pose_landmarks.landmark[11].visibility = 0.1
        self.assertEqual(estimator.update(result, 640, 480, 2), (False, 0.0))
        self.assertEqual(estimator.update(NS(pose_landmarks=None, pose_world_landmarks=None), 640, 480, 3), (False, 0.0))

    def test_recalibrate_at_new_distance(self):
        estimator = self.calibrated()
        estimator.reset()
        for i in range(3):
            valid, offset = estimator.update(person(0.4), 640, 480, i/30)
        self.assertTrue(valid)
        self.assertEqual(offset, 0)

    def test_small_scale_noise_is_ignored(self):
        estimator = self.calibrated()
        for i in range(3, 90):
            valid, offset = estimator.update(person(0.301), 640, 480, i/30)
        self.assertTrue(valid)
        self.assertEqual(offset, 0)

    def test_calibration_requires_multiple_reliable_samples(self):
        estimator = RelativeDepthEstimator(calibration_frames=3)
        self.assertEqual(estimator.update(person(), 640, 480, 0), (False, 0.0))
        bad = person()
        bad.pose_landmarks.landmark[12].x = 1.1
        self.assertEqual(estimator.update(bad, 640, 480, 1), (False, 0.0))
        self.assertEqual(len(estimator.samples), 1)


if __name__ == '__main__':
    unittest.main()
