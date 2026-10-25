import json
import socket
import threading
import unittest
from unittest.mock import Mock, patch
from types import SimpleNamespace as NS

import numpy as np

from body_tracking import BodySender, BodyTracker, make_packet, reject_duplicate_hands


def landmarks(count):
    return NS(landmark=[NS(x=0.25, y=0.5, z=-0.1, visibility=0.8) for _ in range(count)])


class BodyTrackingTests(unittest.TestCase):
    def test_coordinate_conversion_and_anatomical_sides(self):
        result = NS(pose_world_landmarks=landmarks(33),
                    left_hand_landmarks=landmarks(21), right_hand_landmarks=None)
        packet = make_packet(result, 640, 480)
        self.assertEqual(packet['version'], 1)
        self.assertEqual(len(packet['pose']), 33)
        self.assertEqual(len(packet['leftHand']), 21)
        self.assertEqual(packet['rightHand'], [])
        self.assertEqual(packet['pose'][0], dict(x=-0.25, y=-0.5, z=-0.1, visibility=0.8))
        self.assertAlmostEqual(packet['leftHand'][0]['y'], -0.375)
        self.assertEqual(packet['leftHand'][0]['visibility'], 1)

    def test_missing_landmarks_clear_previous_tracking(self):
        result = NS(pose_world_landmarks=None, left_hand_landmarks=None, right_hand_landmarks=None)
        packet = make_packet(result, 640, 480)
        self.assertEqual([packet[k] for k in ('pose', 'leftHand', 'rightHand')], [[], [], []])

    def test_framed_tcp_roundtrip_with_fragmented_reads(self):
        packet = make_packet(NS(pose_world_landmarks=landmarks(33),
                                left_hand_landmarks=landmarks(21), right_hand_landmarks=landmarks(21)), 640, 480)
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            listener.listen(1)
            listener.settimeout(3)
            sender = BodySender(listener.getsockname()[1])
            try:
                peer, _ = listener.accept()
                with peer:
                    peer.settimeout(3)
                    # A separate reader avoids depending on the OS send buffer capacity.
                    frames = []
                    def read():
                        buffer = b''
                        while len(frames) < 2:
                            part = peer.recv(7)
                            if not part:
                                return
                            buffer += part
                            while b'\n' in buffer:
                                line, buffer = buffer.split(b'\n', 1)
                                frames.append(json.loads(line))
                    reader = threading.Thread(target=read)
                    reader.start()
                    sender.send(packet)
                    sender.send(packet)
                    reader.join(5)
                    self.assertFalse(reader.is_alive())
                    self.assertEqual(frames, [packet, packet])
                    with self.assertRaises(ValueError):
                        sender.send(dict(value=float('nan')))
            finally:
                sender.close()

    def test_preview_mirrors_image_landmarks_without_changing_packet(self):
        from mediapipe.framework.formats import landmark_pb2
        hand = landmark_pb2.NormalizedLandmarkList()
        for i in range(21):
            hand.landmark.add(x=0.2 + (i % 4) * 0.01, y=0.4 + (i // 4) * 0.02, z=0)
        result = NS(pose_landmarks=None, pose_world_landmarks=None,
                    left_hand_landmarks=hand, right_hand_landmarks=None)
        tracker = BodyTracker.__new__(BodyTracker)
        tracker.result = result
        before = make_packet(result, 640, 480)
        image = np.zeros((480, 640, 3), dtype=np.uint8)
        returned = tracker.draw(image)
        self.assertIs(returned, image)
        self.assertTrue(image[:, 480:530].any())
        self.assertFalse(image[:, :320].any())
        self.assertEqual(before, make_packet(result, 640, 480))
        tracker.result = NS(pose_landmarks=None, left_hand_landmarks=None, right_hand_landmarks=None)
        blank = np.zeros_like(image)
        tracker.draw(blank)
        self.assertFalse(blank.any())

    def test_duplicate_hands_use_pose_wrists_for_both_preview_and_packet(self):
        from mediapipe.framework.formats import landmark_pb2
        def hand(x):
            points = landmark_pb2.NormalizedLandmarkList()
            for i in range(21):
                points.landmark.add(x=x + (i % 4) * 0.02,
                                   y=0.4 + (i // 4) * 0.02, z=0)
            return points
        left, right = hand(0.2), hand(0.201)
        pose = landmarks(33)
        pose.landmark[15] = NS(x=0.2, y=0.4, visibility=0.9)
        pose.landmark[16] = NS(x=0.8, y=0.4, visibility=0.9)
        result = NS(pose_landmarks=pose, pose_world_landmarks=None,
                    left_hand_landmarks=left, right_hand_landmarks=right)
        filtered = reject_duplicate_hands(result, 640, 480)
        self.assertIs(filtered.left_hand_landmarks, left)
        self.assertIsNone(filtered.right_hand_landmarks)
        self.assertEqual(make_packet(filtered, 640, 480)['rightHand'], [])
        # Reverse the wrist evidence: right hand must win symmetrically.
        pose.landmark[15], pose.landmark[16] = pose.landmark[16], pose.landmark[15]
        filtered = reject_duplicate_hands(result, 640, 480)
        self.assertIsNone(filtered.left_hand_landmarks)
        self.assertIs(filtered.right_hand_landmarks, right)
        # Without reliable wrist evidence neither label is trustworthy.
        pose.landmark[16].visibility = 0.1
        filtered = reject_duplicate_hands(result, 640, 480)
        self.assertIsNone(filtered.left_hand_landmarks)
        self.assertIsNone(filtered.right_hand_landmarks)
        # Distinct hands remain, even when wrists are unreliable.
        result.right_hand_landmarks = hand(0.7)
        self.assertIs(reject_duplicate_hands(result, 640, 480), result)
        # Nearby/crossed wrists with different finger shapes are not duplicates.
        result.right_hand_landmarks = hand(0.2)
        for point in result.right_hand_landmarks.landmark[1:]:
            point.y += 0.1
        self.assertIs(reject_duplicate_hands(result, 640, 480), result)

    def test_real_inference_without_person_emits_empty_arrays(self):
        tracker = BodyTracker()
        try:
            packet = tracker.process(np.zeros((480, 640, 3), dtype=np.uint8))
            self.assertEqual(packet, dict(version=1, pose=[], leftHand=[], rightHand=[], depthValid=False, depthOffset=0.0))
        finally:
            tracker.close()


class MainIntegrationTests(unittest.TestCase):
    def test_body_sends_when_face_is_not_detected_and_releases_resources(self):
        import main
        frame = np.zeros((480, 640, 3), dtype=np.uint8)
        cap = Mock()
        cap.read.side_effect = [(True, frame), (True, frame)]
        cap.isOpened.return_value = True
        detector = Mock()
        detector.findFaceMesh.return_value = (frame, [])
        tracker = Mock()
        tracker.process.return_value = dict(version=1, pose=[], leftHand=[], rightHand=[])
        sender = Mock()
        face_socket = Mock()
        options = NS(cam=0, body=True, connect=True, body_port=5067, debug=False)
        with patch.object(main, 'args', options, create=True), \
             patch.object(main.cv2, 'VideoCapture', return_value=cap), \
             patch.object(main.cv2, 'imshow'), patch.object(main.cv2, 'waitKey', return_value=ord('q')), \
             patch.object(main.cv2, 'destroyAllWindows'), \
             patch.object(main, 'FaceMeshDetector', return_value=detector), \
             patch.object(main, 'init_TCP', return_value=face_socket), \
             patch('body_tracking.BodyTracker', return_value=tracker), \
             patch('body_tracking.BodySender', return_value=sender):
            main.main()
        tracker.draw.assert_called_once_with(frame)
        sender.send.assert_called_once_with(tracker.process.return_value)
        face_socket.send.assert_not_called()
        for resource in (sender, tracker, face_socket):
            resource.close.assert_called_once()
        cap.release.assert_called_once()
        detector.face_mesh.close.assert_called_once()

    def test_camera_failure_is_explicit_and_releases_camera(self):
        import main
        cap = Mock()
        cap.read.return_value = (False, None)
        detector = Mock()
        with patch.object(main, 'args', NS(cam=0, body=False), create=True), \
             patch.object(main.cv2, 'VideoCapture', return_value=cap), \
             patch.object(main.cv2, 'destroyAllWindows'), \
             patch.object(main, 'FaceMeshDetector', return_value=detector):
            with self.assertRaisesRegex(RuntimeError, 'Cannot read camera'):
                main.main()
        cap.release.assert_called_once()
        detector.face_mesh.close.assert_called_once()


if __name__ == '__main__':
    unittest.main()
