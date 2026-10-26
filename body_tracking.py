"""One Holistic inference, temporal tracking, and newline-framed Unity transport."""
from argparse import ArgumentParser
from dataclasses import fields
import json
import socket
import time
from pathlib import Path

import numpy as np

import cv2
import mediapipe as mp
from mediapipe.framework.formats import landmark_pb2
from body_depth import RelativeDepthEstimator
from tracking.pipeline import MotionPipeline, TrackingConfig


def load_config(path=None, raw=False, debug=False):
    values = {}
    if path:
        with open(path, encoding='utf-8') as stream:
            values = json.load(stream)
        # Retired auto-overlap thresholds are accepted only for config migration.
        for key in ('overlap_enter_ratio', 'overlap_exit_ratio', 'overlap_enter_seconds',
                    'overlap_exit_seconds', 'overlap_lost_seconds'):
            values.pop(key, None)
        unknown = set(values)-{field.name for field in fields(TrackingConfig)}
        if unknown:
            raise ValueError('Unknown tracking settings: '+', '.join(sorted(unknown)))
    if raw:
        values['enabled'] = False
    if debug:
        values['debug'] = True
    return TrackingConfig(**values)


class BodyTracker:
    def __init__(self, config=None, include_face=True):
        self.config = config or TrackingConfig()
        self.pipeline = MotionPipeline(self.config)
        self.raw_result = self.packet = None
        self.depth = RelativeDepthEstimator()
        self.model = mp.solutions.holistic.Holistic(
            model_complexity=1, refine_face_landmarks=include_face,
            smooth_landmarks=False,
            min_detection_confidence=0.5, min_tracking_confidence=0.5)

    def process(self, frame, timestamp=None):
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        rgb.flags.writeable = False
        self.raw_result = self.model.process(rgb)
        now = time.monotonic() if timestamp is None else timestamp
        self.packet = self.pipeline.process(self.raw_result, frame.shape[1], frame.shape[0], now)
        valid, offset = self.depth.update(self.raw_result, frame.shape[1], frame.shape[0], now)
        self.packet.update(depthValid=valid, depthOffset=offset)
        return self.packet

    def reset_depth(self):
        self.depth.reset()

    def cycle_overlap_mode(self):
        modes = ('off', 'force')
        self.config.overlap_mode = modes[(modes.index(self.config.overlap_mode)+1) % len(modes)]
        print('Hand overlap mode:', self.config.overlap_mode)

    def face_detector(self):
        return HolisticFaceAdapter(self)

    def draw(self, mirrored_frame):
        if self.packet is None:
            return mirrored_frame
        canvas = cv2.flip(mirrored_frame, 1)
        drawing = mp.solutions.drawing_utils
        holistic = mp.solutions.holistic
        aspect = canvas.shape[1]/canvas.shape[0]
        if self.config.debug and self.raw_result is not None:
            for attr, edges in [('pose_landmarks', holistic.POSE_CONNECTIONS),
                                ('left_hand_landmarks', holistic.HAND_CONNECTIONS),
                                ('right_hand_landmarks', holistic.HAND_CONNECTIONS)]:
                landmarks = getattr(self.raw_result, attr, None)
                if landmarks is not None:
                    style = drawing.DrawingSpec(color=(100, 100, 100), thickness=1, circle_radius=1)
                    drawing.draw_landmarks(canvas, landmarks, edges, style, style)
        for name, edges, color in [('imagePose', holistic.POSE_CONNECTIONS, (0, 255, 0)),
                                    ('leftHand', holistic.HAND_CONNECTIONS, (255, 180, 0)),
                                    ('rightHand', holistic.HAND_CONNECTIONS, (0, 140, 255))]:
            points = self.packet[name]
            if points:
                landmarks = landmark_pb2.NormalizedLandmarkList()
                for point in points:
                    landmarks.landmark.add(x=-point['x'], y=-point['y']*aspect,
                                           z=point['z'], visibility=point['visibility'])
                style = drawing.DrawingSpec(color=color, thickness=2, circle_radius=3)
                drawing.draw_landmarks(canvas, landmarks, edges, style, style)
        mirrored_frame[:] = cv2.flip(canvas, 1)
        if self.config.debug:
            status = self.packet['trackingStatus']
            cv2.putText(mirrored_frame, 'L: '+status['left']+'  R: '+status['right']+(' | OVERLAP approximation' if self.packet['overlapActive'] else ''),
                        (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
            pose = getattr(self.raw_result, 'pose_landmarks', None)
            confidences = 'no body wrists' if pose is None else 'body wrist vis L %.2f R %.2f' % (
                pose.landmark[15].visibility, pose.landmark[16].visibility)
            cv2.putText(mirrored_frame, status['pose']+' | '+confidences,
                        (10, 47), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        return mirrored_frame

    def close(self):
        self.model.close()


_FACE_MIRROR = None


def face_mirror_map():
    global _FACE_MIRROR
    if _FACE_MIRROR is None:
        # Canonical mesh symmetry gives a bijective anatomical left/right map.
        # A mere x reflection leaves a reflected (non-rotational) PnP fit.
        points = np.loadtxt(Path(__file__).with_name('model.txt')).reshape(468, 3)
        reflected = points.copy(); reflected[:, 0] *= -1
        distance = np.sum((reflected[:, None]-points[None, :])**2, axis=2)
        mapping = distance.argmin(axis=1)
        if len(set(mapping)) != 468 or not np.all(mapping[mapping] == np.arange(468)):
            raise ValueError('Canonical face model has no bijective mirror mapping')
        # Iris centers exchange sides; horizontal ring points reverse order.
        _FACE_MIRROR = list(mapping) + [473, 476, 475, 474, 477, 468, 471, 470, 469, 472]
    return _FACE_MIRROR


class HolisticFaceAdapter:
    """Existing face pipeline expects a mirrored image and 478 pixel landmarks.

    Holistic receives an UNMIRRORED camera frame. Mirror only its image-space
    face landmarks here, never the anatomical body/hand identity pipeline.
    """
    def __init__(self, tracker):
        self.tracker = tracker
        self.fallback = None

    def findFaceMesh(self, frame, draw=True):
        image = cv2.flip(frame, 1)
        source = getattr(self.tracker.raw_result, 'face_landmarks', None)
        if source is None or len(source.landmark) != 478:
            # Holistic's face ROI depends on pose. Preserve the original tight
            # face-only workflow without paying for FaceMesh on successful frames.
            if self.fallback is None:
                from facial_landmark import FaceMeshDetector
                self.fallback = FaceMeshDetector()
            return self.fallback.findFaceMesh(frame, draw)
        mirrored = landmark_pb2.NormalizedLandmarkList()
        for index in face_mirror_map():
            point = mirrored.landmark.add()
            point.CopyFrom(source.landmark[int(index)])
            point.x = 1-point.x
        if draw:
            mp.solutions.drawing_utils.draw_landmarks(image, mirrored,
                mp.solutions.face_mesh.FACEMESH_TESSELATION,
                mp.solutions.drawing_utils.DrawingSpec(thickness=1, circle_radius=1))
        height, width = frame.shape[:2]
        return image, [[[int(p.x*width), int(p.y*height)] for p in mirrored.landmark]]

    def close(self):
        if self.fallback is not None:
            self.fallback.close()
            self.fallback = None
        # The owning BodyTracker closes the shared Holistic model exactly once.


class BodySender:
    def __init__(self, port):
        self.socket = socket.create_connection(('127.0.0.1', port), timeout=3)
        self.socket.settimeout(1)

    def send(self, packet):
        self.socket.sendall((json.dumps(packet, allow_nan=False, separators=(',', ':'))+'\n').encode('ascii'))

    def close(self):
        self.socket.close()


def preview():
    parser = ArgumentParser(description='Preview body, arm and finger tracking. Q quits; C recalibrates depth.')
    parser.add_argument('--cam', type=int, default=0)
    parser.add_argument('--video', help='Replay a recorded video instead of a webcam')
    parser.add_argument('--tracking-config', help='JSON TrackingConfig overrides')
    parser.add_argument('--raw-tracking', action='store_true', help='Disable adaptive filtering for comparison')
    parser.add_argument('--debug-tracking', action='store_true', help='Overlay raw landmarks and identity states')
    args = parser.parse_args()
    cap = cv2.VideoCapture(args.video or args.cam)
    tracker = None
    try:
        if not cap.isOpened():
            raise RuntimeError('Cannot open camera/video; check --cam/--video and permissions')
        tracker = BodyTracker(load_config(args.tracking_config, args.raw_tracking, args.debug_tracking), include_face=False)
        while True:
            success, frame = cap.read()
            if not success:
                if args.video:
                    break
                raise RuntimeError('Cannot read camera frame')
            timestamp = cap.get(cv2.CAP_PROP_POS_MSEC)/1000 if args.video else None
            tracker.process(frame, timestamp)
            image = tracker.draw(cv2.flip(frame, 1))
            cv2.imshow('Body and hands - Q to quit', image)
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                break
            if key == ord('c'):
                tracker.reset_depth()
            if key == ord('i'):
                tracker.cycle_overlap_mode()
    finally:
        cap.release()
        if tracker:
            tracker.close()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    preview()
