"""Optional CPU body/hand tracking and newline-framed Unity transport."""
import json
import socket

import cv2
import mediapipe as mp


def landmarks_to_points(landmarks, aspect=1.0, visibility=False):
    """Unmirrored MediaPipe coordinates -> avatar-facing Unity coordinates.

    Hand z uses the same units as image x; correct image y for aspect ratio.
    Pose world landmarks already use metric coordinates.
    """
    if landmarks is None:
        return []
    return [dict(x=-p.x, y=-p.y / aspect, z=p.z,
                 visibility=p.visibility if visibility else 1.0)
            for p in landmarks.landmark]


def make_packet(results, width, height):
    return dict(version=1,
                pose=landmarks_to_points(results.pose_world_landmarks, visibility=True),
                leftHand=landmarks_to_points(results.left_hand_landmarks, width / height),
                rightHand=landmarks_to_points(results.right_hand_landmarks, width / height))


class BodyTracker:
    def __init__(self):
        self.model = mp.solutions.holistic.Holistic(
            model_complexity=1, smooth_landmarks=True,
            min_detection_confidence=0.5, min_tracking_confidence=0.5)

    def process(self, frame):
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        rgb.flags.writeable = False
        result = self.model.process(rgb)
        return make_packet(result, frame.shape[1], frame.shape[0])

    def close(self):
        self.model.close()


class BodySender:
    def __init__(self, port):
        self.socket = socket.create_connection(('127.0.0.1', port), timeout=3)
        self.socket.settimeout(1)

    def send(self, packet):
        self.socket.sendall((json.dumps(packet, allow_nan=False, separators=(',', ':')) + '\n').encode('ascii'))

    def close(self):
        self.socket.close()
