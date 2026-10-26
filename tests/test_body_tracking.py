import json
import socket
import threading
import unittest
from unittest.mock import Mock, patch
from types import SimpleNamespace as NS
import numpy as np
from body_tracking import BodySender, BodyTracker, HolisticFaceAdapter, load_config, face_mirror_map


class BodyTrackingTests(unittest.TestCase):
    def test_framed_tcp_roundtrip_with_fragmented_reads(self):
        packet = dict(version=1, pose=[], leftHand=[], rightHand=[])
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

    def test_real_model_and_shared_face_adapter(self):
        tracker = BodyTracker()
        try:
            packet = tracker.process(np.zeros((480, 640, 3), dtype=np.uint8), 1.0)
            self.assertEqual(packet['pose'], [])
            self.assertEqual(packet['leftHand'], [])
            self.assertTrue(packet['adaptiveFiltered'])
            self.assertFalse(packet['depthValid'])
            adapter = tracker.face_detector()
            _, faces = adapter.findFaceMesh(np.zeros((480, 640, 3), dtype=np.uint8))
            self.assertEqual(faces, [])
            adapter.close()
        finally:
            tracker.close()

    def test_face_adapter_mirrors_without_second_inference(self):
        from mediapipe.framework.formats import landmark_pb2
        face = landmark_pb2.NormalizedLandmarkList()
        for _ in range(478):
            face.landmark.add(x=0.25, y=0.5, z=0)
        tracker = NS(raw_result=NS(face_landmarks=face))
        adapter = HolisticFaceAdapter(tracker)
        image = np.zeros((480, 640, 3), dtype=np.uint8)
        _, faces = adapter.findFaceMesh(image, draw=False)
        self.assertEqual(faces[0][0], [480, 240])
        self.assertEqual(face.landmark[0].x, 0.25)
        adapter.close()

    def test_face_mirror_is_bijective_and_swaps_anatomical_eyes(self):
        mapping = face_mirror_map()
        self.assertEqual(len(set(mapping)), 478)
        self.assertEqual([mapping[mapping[i]] for i in range(478)], list(range(478)))
        self.assertEqual(mapping[33], 263)
        self.assertEqual(mapping[468], 473)
        self.assertEqual(mapping[473], 468)

    def test_real_face_reuse_matches_original_mirrored_detector(self):
        from pathlib import Path
        import cv2
        from PIL import Image
        from facial_landmark import FaceMeshDetector
        with Image.open(Path(__file__).resolve().parents[1]/'Images/live2d_demo.gif') as image:
            frame = np.asarray(image.convert('RGB'))[:, :, ::-1].copy()[85:348, 490:837]
        detector = FaceMeshDetector(static_image_mode=True)
        try:
            _, mirrored = detector.findFaceMesh(frame, draw=False)
            detector.findFaceMesh(cv2.flip(frame, 1), draw=False)
            source = detector.results.multi_face_landmarks[0]
            adapter = HolisticFaceAdapter(NS(raw_result=NS(face_landmarks=source)))
            _, reused = adapter.findFaceMesh(frame, draw=False)
            error = np.sqrt(np.mean((np.asarray(mirrored[0][:468])-np.asarray(reused[0][:468]))**2))
            self.assertLess(error, 8.0)
            adapter.close()
        finally:
            detector.close()

    def test_config_preserves_defaults_and_disable(self):
        from pathlib import Path
        self.assertEqual(load_config(str(Path(__file__).resolve().parents[1]/'tracking.defaults.json')), load_config())
        self.assertTrue(load_config().enabled)
        self.assertFalse(load_config(raw=True).enabled)


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
        tracker.face_detector.return_value = detector
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
        detector.close.assert_called_once()

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
        detector.close.assert_called_once()


if __name__ == '__main__':
    unittest.main()
