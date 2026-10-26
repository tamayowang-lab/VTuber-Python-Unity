"""
Main program to run the detection and TCP
"""

from argparse import ArgumentParser
import cv2
import mediapipe as mp
import numpy as np

# for TCP connection with unity
import socket

# face detection and facial landmark
from facial_landmark import FaceMeshDetector

# pose estimation and stablization
from pose_estimator import PoseEstimator
from head_pose import HeadOrientation

# Miscellaneous detections (eyes/ mouth...)
from facial_features import FacialFeatures, Eyes

import sys

# global variable
port = 5066         # have to be same as unity

# init TCP connection with unity
# return the socket connected
def init_TCP():
    port = args.port

    # '127.0.0.1' = 'localhost' = your computer internal data transmission IP
    address = ('127.0.0.1', port)
    # address = ('192.168.0.107', port)

    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.connect(address)
        # print(socket.gethostbyname(socket.gethostname()) + "::" + str(port))
        print("Connected to address:", socket.gethostbyname(socket.gethostname()) + ":" + str(port))
        return s
    except OSError as e:
        print("Error while connecting :: %s" % e)

        # quit the script if connection fails (e.g. Unity server side quits suddenly)
        sys.exit()

    # s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    # # print(socket.gethostbyname(socket.gethostname()))
    # s.connect(address)
    # return s

def send_info_to_unity(s, args):
    msg = '%.4f ' * len(args) % args

    try:
        s.send(bytes(msg, "utf-8"))
    except socket.error as e:
        print("error while sending :: " + str(e))

        # quit the script if connection fails (e.g. Unity server side quits suddenly)
        sys.exit()

def print_debug_msg(args):
    msg = '%.4f ' * len(args) % args
    print(msg)

def main():

    # use internal webcam/ USB camera
    cap = cv2.VideoCapture(getattr(args, 'video', None) or args.cam)

    body_tracker = body_sender = detector = face_socket = None
    try:
        # IP cam (android only), with the app "IP Webcam"
        # url = 'http://192.168.0.102:4747/video'
        # url = 'https://192.168.0.102:8080/video'
        # cap = cv2.VideoCapture(url)

        if args.body:
            from body_tracking import BodyTracker, BodySender, load_config
            body_tracker = BodyTracker(load_config(getattr(args, 'tracking_config', None),
                getattr(args, 'raw_tracking', False), getattr(args, 'debug_tracking', False)))
            if args.connect:
                try:
                    body_sender = BodySender(args.body_port)
                except OSError:
                    raise RuntimeError("Start HumanoidBodyController in Unity on --body-port first")

        # Facemesh
        detector = body_tracker.face_detector() if body_tracker else FaceMeshDetector()

        # get a sample frame for pose estimation img
        success, img = cap.read()

        if not success or img is None:
            raise RuntimeError("Cannot read camera; check --cam and camera permissions")

        # Pose estimation related
        pose_estimator = PoseEstimator((img.shape[0], img.shape[1]))
        head_orientation = HeadOrientation(getattr(args, 'head_calibration_frames', 12))
        image_points = np.zeros((pose_estimator.model_points_full.shape[0], 2))

        # extra 10 points due to new attention model (in iris detection)
        iris_image_points = np.zeros((10, 2))

        # Initialize TCP connection
        if args.connect:
            face_socket = init_TCP()

        while cap.isOpened():
            success, img = cap.read()

            if not success:
                print("Ignoring empty camera frame.")
                break

            if body_tracker:
                body_packet = body_tracker.process(img, cap.get(cv2.CAP_PROP_POS_MSEC)/1000
                    if getattr(args, 'video', None) else None)
                if body_sender:
                    body_sender.send(body_packet)
                if args.debug:
                    print("Body: %d pose, %d left hand, %d right hand landmarks" % (
                        len(body_packet['pose']), len(body_packet['leftHand']), len(body_packet['rightHand'])))

            # Pose estimation by 3 steps:
            # 1. detect face;
            # 2. detect landmarks;
            # 3. estimate pose

            # first two steps
            img_facemesh, faces = detector.findFaceMesh(img)

            # flip the input image so that it matches the facemesh stuff
            img = cv2.flip(img, 1)

            # if there is any face detected
            if faces:
                # only get the first face
                for i in range(len(image_points)):
                    image_points[i, 0] = faces[0][i][0]
                    image_points[i, 1] = faces[0][i][1]

                # for refined landmarks around iris
                for j in range(len(iris_image_points)):
                    iris_image_points[j, 0] = faces[0][j + 468][0]
                    iris_image_points[j, 1] = faces[0][j + 468][1]

                # The third step: pose estimation
                # pose: [[rvec], [tvec]]
                pose = pose_estimator.solve_pose_by_all_points(image_points)

                x_ratio_left, y_ratio_left = FacialFeatures.detect_iris(image_points, iris_image_points, Eyes.LEFT)
                x_ratio_right, y_ratio_right = FacialFeatures.detect_iris(image_points, iris_image_points, Eyes.RIGHT)


                ear_left = FacialFeatures.eye_aspect_ratio(image_points, Eyes.LEFT)
                ear_right = FacialFeatures.eye_aspect_ratio(image_points, Eyes.RIGHT)


                mar = FacialFeatures.mouth_aspect_ratio(image_points)
                mouth_distance = FacialFeatures.mouth_distance(image_points)

                roll, pitch, yaw = head_orientation.angles(pose[0])

                # send info to unity
                if args.connect:

                    # for sending to live2d model (Hiyori)
                    send_info_to_unity(face_socket,
                        (roll, pitch, yaw,
                        ear_left, ear_right, x_ratio_left, y_ratio_left, x_ratio_right, y_ratio_right,
                        mar, mouth_distance)
                    )

                # print the sent values in the terminal
                if args.debug:
                    print_debug_msg((roll, pitch, yaw,
                            ear_left, ear_right, x_ratio_left, y_ratio_left, x_ratio_right, y_ratio_right,
                            mar, mouth_distance))


                pose_estimator.draw_axes(img_facemesh, pose[0], pose[1])

            else:
                # reset our pose estimator
                pose_estimator = PoseEstimator((img_facemesh.shape[0], img_facemesh.shape[1]))

            if body_tracker:
                body_tracker.draw(img_facemesh)
            if faces and head_orientation.neutral is None:
                cv2.putText(img_facemesh, 'Head calibration: look straight and hold still',
                    (8, img_facemesh.shape[0]-12), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1)
            cv2.imshow('Face, body and hands' if args.body else 'Facial landmark', img_facemesh)

            # press "q" to leave
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                break
            if key == ord('c'):
                head_orientation.reset()
                if body_tracker:
                    body_tracker.reset_depth()
                print("Head neutral reset: face forward for the next detected frame.")

    finally:
        cap.release()
        cv2.destroyAllWindows()
        if detector:
            detector.close()
        if face_socket:
            face_socket.close()
        if body_tracker:
            body_tracker.close()
        if body_sender:
            body_sender.close()


if __name__ == "__main__":

    parser = ArgumentParser()

    parser.add_argument("--connect", action="store_true",
                        help="connect to unity character",
                        default=False)

    parser.add_argument("--port", type=int,
                        help="specify the port of the connection to unity. Have to be the same as in Unity",
                        default=5066)

    parser.add_argument("--cam", type=int,
                        help="specify the camera number if you have multiple cameras",
                        default=0)

    parser.add_argument("--debug", action="store_true",
                        help="showing raw values of detection in the terminal",
                        default=False)

    parser.add_argument("--body", action="store_true",
                        help="also track torso, arms and fingers for a Humanoid avatar")
    parser.add_argument("--body-port", type=int, default=5067,
                        help="HumanoidBodyController TCP port (default: 5067)")

    parser.add_argument("--tracking-config", help="JSON tracking configuration")
    parser.add_argument("--raw-tracking", action="store_true", help="Disable adaptive landmark smoothing")
    parser.add_argument("--debug-tracking", action="store_true", help="Show raw/filtered landmarks and identity states")
    parser.add_argument("--video", help="Use a recorded video instead of the webcam")

    parser.add_argument("--head-calibration-frames", type=int, default=12,
                        help="Stable face samples used for head neutral (default 12)")

    args = parser.parse_args()

    # demo code
    main()
