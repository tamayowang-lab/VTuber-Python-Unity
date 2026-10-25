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
        self.result = None
        self.model = mp.solutions.holistic.Holistic(
            model_complexity=1, smooth_landmarks=True,
            min_detection_confidence=0.5, min_tracking_confidence=0.5)

    def process(self, frame):
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        rgb.flags.writeable = False
        self.result = self.model.process(rgb)
        return make_packet(self.result, frame.shape[1], frame.shape[0])

    def draw(self, mirrored_frame):
        """Draw this frame's image landmarks on the mirrored face preview.

        Keep image coordinates separate from the metric Unity pose packet.
        Draw before flipping so handedness and overlay positions stay aligned.
        """
        if self.result is None:
            return mirrored_frame
        canvas = cv2.flip(mirrored_frame, 1)
        drawing = mp.solutions.drawing_utils
        holistic = mp.solutions.holistic
        for landmarks, connections, color in (
            (self.result.pose_landmarks, holistic.POSE_CONNECTIONS, (0, 255, 0)),
            (self.result.left_hand_landmarks, holistic.HAND_CONNECTIONS, (255, 180, 0)),
            (self.result.right_hand_landmarks, holistic.HAND_CONNECTIONS, (0, 140, 255)),
        ):
            if landmarks is not None:
                drawing.draw_landmarks(canvas, landmarks, connections,
                    drawing.DrawingSpec(color=color, thickness=2, circle_radius=3),
                    drawing.DrawingSpec(color=color, thickness=2))
        mirrored_frame[:] = cv2.flip(canvas, 1)
        return mirrored_frame

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


def preview():
    """Standalone camera preview, with no Unity connection required."""
    from argparse import ArgumentParser
    parser = ArgumentParser(description="Preview body, arm and finger tracking. Press Q to quit.")
    parser.add_argument('--cam', type=int, default=0)
    args = parser.parse_args()
    cap = cv2.VideoCapture(args.cam)
    tracker = None
    try:
        if not cap.isOpened():
            raise RuntimeError("Cannot open camera; check --cam and camera permissions")
        tracker = BodyTracker()
        while True:
            success, frame = cap.read()
            if not success:
                raise RuntimeError("Cannot read camera frame")
            tracker.process(frame)
            image = tracker.draw(cv2.flip(frame, 1))
            cv2.imshow('Body and hands - Q to quit', image)
            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
    finally:
        cap.release()
        if tracker:
            tracker.close()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    preview()
