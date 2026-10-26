"""Identity before smoothing; uncertain data is withheld, never relabelled by screen side."""
from dataclasses import dataclass
from itertools import product
import numpy as np
from .filtering import OneEuroFilter
from .overlap import ManualHandPoseCue


@dataclass
class TrackingConfig:
    enabled: bool = True
    min_cutoff: float = 1.2
    beta: float = 2.0
    finger_min_cutoff: float = 1.8
    finger_beta: float = 2.5
    derivative_cutoff: float = 1.0
    min_visibility: float = 0.6
    prediction_seconds: float = 0.08
    identity_memory: float = 0.5
    max_image_speed: float = 4.0
    max_world_speed: float = 8.0
    debug: bool = False
    overlap_mode: str = 'off'

    def __post_init__(self):
        if self.overlap_mode == 'auto':
            # Safe migration: old configs must not silently replace visible fingers.
            self.overlap_mode = 'off'
        if self.overlap_mode not in ('off', 'force'):
            raise ValueError('overlap_mode must be off or force (manual animation only)')
        for name in ('min_cutoff', 'finger_min_cutoff', 'max_image_speed', 'max_world_speed', 'identity_memory', 'derivative_cutoff'):
            if getattr(self, name) <= 0:
                raise ValueError(name+' must be positive')
        if self.beta < 0 or self.finger_beta < 0 or not 0 <= self.min_visibility <= 1:
            raise ValueError('Invalid beta/visibility')
        if not 0 <= self.prediction_seconds <= min(0.2, self.identity_memory):
            raise ValueError('Prediction must be bounded to 0..0.2 seconds and identity memory')


def array(landmarks):
    if landmarks is None:
        return None
    return np.array([(p.x, p.y, p.z) for p in landmarks.landmark], dtype=float).reshape(-1, 3)


def packet_points(points, aspect=1.0, visibility=None):
    if points is None:
        return []
    if visibility is None:
        visibility = np.ones(len(points))
    # Wire v1 intentionally retains raw camera z; Unity invertDepth performs
    # the final front/back conversion exactly once. Never mix world and image units.
    return [dict(x=-float(p[0]), y=-float(p[1])/aspect, z=float(p[2]), visibility=float(v))
            for p, v in zip(points, visibility)]


class HandTrack:
    def __init__(self, config):
        self.config = config
        self.filter = OneEuroFilter(config.finger_min_cutoff, config.finger_beta, config.derivative_cutoff)
        self.raw = self.filtered = None
        self.time = None
        self.velocity = np.zeros(3)
        self.visibility = np.ones(21)

    def expected(self, now):
        horizon = min(max(now-self.time, 0), self.config.prediction_seconds)
        return self.raw[0] + self.velocity*horizon

    def accept(self, points, now, freeze_fingers=False):
        visibility = np.ones(21)
        palm = max(np.linalg.norm(points[9]-points[0]), np.linalg.norm(points[5]-points[17]))
        if palm < 1e-5 or palm > 1.0:
            return None, 'implausible'
        points = points.copy()
        dt = now-self.time if self.time is not None else float('inf')
        if self.raw is not None and 0 < dt <= self.config.identity_memory:
            if np.linalg.norm(points[0]-self.expected(now)) > 0.08+self.config.max_image_speed*dt:
                return None, 'jump-rejected'
        if self.raw is not None and 0 < dt < 0.15:
            previous_palm = max(np.linalg.norm(self.raw[9]-self.raw[0]), np.linalg.norm(self.raw[5]-self.raw[17]), 1e-5)
            if not 0.55 <= palm/previous_palm <= 1.8:
                return None, 'geometry-jump'
        if self.raw is not None and 0 < dt <= self.config.identity_memory:
            before = np.cross(self.raw[5]-self.raw[0], self.raw[17]-self.raw[0])
            after = np.cross(points[5]-points[0], points[17]-points[0])
            if min(np.linalg.norm(before), np.linalg.norm(after)) > palm*palm*0.05:
                cosine = np.dot(before, after)/(np.linalg.norm(before)*np.linalg.norm(after))
                angle = np.degrees(np.arccos(np.clip(cosine, -1, 1)))
                if angle > 30+720*dt:
                    return None, 'orientation-jump'
        for finger in range(5):
            indices = list(range(1+finger*4, 5+finger*4))
            segments = np.linalg.norm(np.diff(points[indices], axis=0), axis=1)
            valid = np.all((segments > 0.02*palm) & (segments < 1.3*palm))
            if self.raw is not None and (self.visibility[indices] > 0.5).all() and 0 < dt <= self.config.identity_memory:
                change = (points[indices]-points[0])-(self.raw[indices]-self.raw[0])
                valid &= np.max(np.linalg.norm(change, axis=1)) <= palm*(0.6+12*dt)
            if not valid:
                base = points[indices[0]].copy()
                base_reliable = self.raw is None or dt > self.config.identity_memory or (
                    np.linalg.norm((base-points[0])-(self.raw[indices[0]]-self.raw[0])) <= palm*(0.6+12*dt))
                visibility[indices] = 0
                if self.raw is not None:
                    points[indices] = self.raw[indices] + points[0]-self.raw[0]
                else:
                    points[indices] = points[0]  # Finite placeholder, explicitly invisible.
                if base_reliable:
                    # A bad fingertip must not erase a valid MCP used for palm orientation.
                    points[indices[0]] = base
                    visibility[indices[0]] = 1
            elif self.filter.time is not None and not (self.visibility[indices] > 0.5).all():
                # Recovered fingers must not interpolate through a previous
                # rejected observation or remain permanently jump-gated.
                self.filter.raw[indices] = points[indices]
                self.filter.value[indices] = points[indices]
                self.filter.derivative[indices] = 0
        if freeze_fingers:
            for i in range(1, 21):
                if i in (5, 9, 17):
                    continue  # Keep measured palm bases, not the hidden curls.
                visibility[i] = 0
                points[i] = self.raw[i]+points[0]-self.raw[0] if self.raw is not None else points[0]
        if self.raw is not None and 0 < dt < 0.25:
            speed = (points[0]-self.raw[0])/dt
            norm = np.linalg.norm(speed)
            self.velocity = speed*min(1, self.config.max_image_speed/max(norm, 1e-6))
        else:
            self.velocity[:] = 0
        self.filtered = self.filter.update(points, now) if self.config.enabled else points.copy()
        self.raw, self.time, self.visibility = points, now, visibility
        return self.filtered, 'tracked' if visibility.min() else 'partial'

    def missing(self, now):
        if self.time is None or now-self.time > self.config.prediction_seconds:
            return None
        # Translate the last shape for only a brief interval, never extrapolate curls.
        delta = self.velocity*max(0, now-self.time)
        return self.filtered+delta


class MotionPipeline:
    def __init__(self, config=None):
        self.config = config or TrackingConfig()
        self.hands = [HandTrack(self.config), HandTrack(self.config)]
        self.pose_filter = OneEuroFilter(self.config.min_cutoff, self.config.beta, self.config.derivative_cutoff)
        self.image_filter = OneEuroFilter(self.config.min_cutoff, self.config.beta, self.config.derivative_cutoff)
        self.previous_pose = self.pose_time = self.previous_image = None
        self.overlap = ManualHandPoseCue(self.config)
        self.status = {}
        self.last_time = self.last_packet = None
        self.outlier_pose = None
        self.outlier_counts = np.zeros(33, dtype=int)

    def assign(self, candidates, pose, now, aspect):
        def distance(a, b):
            return np.linalg.norm((a-b)[:2])
        anchors = []
        for side, index in enumerate((15, 16)):
            anchor = None
            if pose is not None:
                def usable(i):
                    p = pose.landmark[i]
                    return p.visibility >= self.config.min_visibility and 0 <= p.x <= 1 and 0 <= p.y <= 1
                if usable(index):
                    p = pose.landmark[index]
                    anchor = (np.array([p.x, p.y/aspect, p.z]), 0.65)
                elif usable(13+side) and usable(11+side):
                    a, b = pose.landmark[11+side], pose.landmark[13+side]
                    shoulder, elbow = np.array([a.x, a.y/aspect, a.z]), np.array([b.x, b.y/aspect, b.z])
                    anchor = (elbow+0.8*(elbow-shoulder), 0.25)
            anchors.append(anchor)
        # Suppress duplicates BEFORE assignment so one physical hand cannot
        # occupy both tracks. Without a distinguishing anchor/history, abstain.
        if len(candidates) == 2:
            a, b = candidates[0][1], candidates[1][1]
            palm = min(np.linalg.norm(a[9]-a[0]), np.linalg.norm(b[9]-b[0]))
            if np.sqrt(np.mean(np.sum((a-b)**2, axis=1))) < 0.4*max(palm, 1e-5):
                candidates = [candidates[0]]
        costs = np.full((2, len(candidates)), 10.0)
        for side, track in enumerate(self.hands):
            memory = track.time is not None and (self.overlap.active or now-track.time <= self.config.identity_memory)
            for c, (label, points) in enumerate(candidates):
                evidence = []
                if anchors[side] is not None:
                    evidence.append((distance(points[0], anchors[side][0]), anchors[side][1]))
                if memory:
                    weight = 0.25 if self.overlap.active and anchors[side] is not None else 0.8
                    evidence.append((distance(points[0], track.expected(now)), weight))
                if evidence:
                    costs[side, c] = sum(d*w for d, w in evidence)/sum(w for _, w in evidence)
                    costs[side, c] += 0.015*(label != side)  # Holistic semantic side is only a weak prior.
                else:
                    costs[side, c] = 0.28 if label == side else 0.34
        assignments = []
        for left, right in product(range(-1, len(candidates)), repeat=2):
            if left >= 0 and left == right:
                continue
            cost = sum(0.3 if c < 0 else costs[s, c] for s, c in enumerate((left, right)))
            assignments.append((cost, (left, right)))
        assignments.sort(key=lambda item: item[0])
        chosen = list(assignments[0][1])
        if len(assignments) > 1 and assignments[1][0]-assignments[0][0] < 0.025:
            alternative = assignments[1][1]
            chosen = [c if c == alternative[s] else -1 for s, c in enumerate(chosen)]
        for side, c in enumerate(chosen):
            if c >= 0 and costs[side, c] > 0.3:
                chosen[side] = -1
        # Overlapping duplicate with no useful body/temporal evidence is not
        # proof of either anatomical side; avoid arbitrary colour switching.
        if len(candidates) == 1 and all(a is None for a in anchors) and all(t.time is None for t in self.hands):
            if len(self.raw_candidates) == 2:
                chosen = [-1, -1]
        return [None if c < 0 else candidates[c][1] for c in chosen]

    def process(self, result, width, height, now):
        if not np.isfinite(now):
            raise ValueError('Timestamp must be finite')
        if self.last_time is not None and now <= self.last_time:
            return dict(self.last_packet)
        aspect = width/height
        pose_image = getattr(result, 'pose_landmarks', None)
        candidates = []
        observations = ['missing', 'missing']
        for label, attr in enumerate(('left_hand_landmarks', 'right_hand_landmarks')):
            points = array(getattr(result, attr, None))
            if points is not None:
                # Hand z is width-normalized. Put y in the same units BEFORE
                # identity, geometry checks, derivatives and filtering.
                points[:, 1] /= aspect
                if points.shape == (21, 3) and np.isfinite(points[[0, 5, 9, 17]]).all():
                    valid = np.isfinite(points).all(axis=1)
                    if not valid.all():
                        points[~valid] = points[0]
                    candidates.append((label, points))
                    observations[label] = 'observed' if valid.all() else 'partial-nonfinite'
                else:
                    observations[label] = 'nonfinite-or-malformed'
        self.raw_candidates = candidates
        overlap_active, overlap_wrists = self.overlap.update(pose_image, aspect)
        assigned = self.assign(candidates, pose_image, now, aspect)
        hands, states = [], []
        for side, (track, points) in enumerate(zip(self.hands, assigned)):
            accepted, state = (None, 'missing') if points is None else track.accept(points, now, overlap_active)
            if accepted is None:
                accepted = track.missing(now)
                if accepted is not None:
                    state = 'predicted'
            visibility = track.visibility.copy()
            if overlap_active and accepted is None and track.filtered is not None and overlap_wrists[side] is not None:
                # A body-wrist observation can translate a HELD palm; it is not
                # a newly observed hand and never extrapolates finger bends.
                accepted = track.filtered.copy()
                accepted[:, :2] += overlap_wrists[side][:2]-accepted[0, :2]
                visibility[:] = 0
                visibility[[0, 5, 9, 17]] = 0.65
                state = 'overlap-held'
            if state == 'predicted':
                visibility *= 0.65
            if overlap_active and accepted is not None:
                visibility[[i for i in range(1, 21) if i not in (5, 9, 17)]] = 0
                if state not in ('overlap-held', 'predicted'):
                    state = 'overlap-palm'
            hands.append(packet_points(accepted, visibility=visibility))
            states.append(state)
        world = array(getattr(result, 'pose_world_landmarks', None))
        pose_status = 'missing'
        visibility = None
        if world is not None and world.shape == (33, 3):
            visibility = np.array([p.visibility for p in result.pose_world_landmarks.landmark])
            finite = np.isfinite(world).all(axis=1) & np.isfinite(visibility)
            plausible = (np.abs(world) <= 10).all(axis=1)
            good = finite & plausible & (visibility >= self.config.min_visibility)
            pose_status = 'tracked' if good.all() else 'low-confidence'
            if not finite.all(): pose_status = 'nonfinite'
            elif not plausible.all(): pose_status = 'implausible'
            for shoulder, elbow, wrist in ((11, 13, 15), (12, 14, 16)):
                if good[shoulder] and good[elbow] and not 0.04 <= np.linalg.norm(world[elbow]-world[shoulder]) <= 0.9:
                    good[elbow] = good[wrist] = False
                    pose_status = 'implausible'
                elif good[elbow] and good[wrist] and not 0.04 <= np.linalg.norm(world[wrist]-world[elbow]) <= 0.9:
                    good[wrist] = False
                    pose_status = 'implausible'
            if self.previous_pose is not None and self.pose_time is not None and 0 < now-self.pose_time < 0.5:
                jump = np.linalg.norm(world-self.previous_pose, axis=1)
                outlier = good & (jump > 0.08+self.config.max_world_speed*(now-self.pose_time))
                consistent = np.zeros(33, dtype=bool) if self.outlier_pose is None else (
                    np.linalg.norm(world-self.outlier_pose, axis=1) < 0.04)
                self.outlier_counts = np.where(outlier, np.where(consistent, self.outlier_counts+1, 1), 0)
                self.outlier_pose = world.copy()
                good &= ~outlier | (self.outlier_counts >= 3)
                if (outlier & (self.outlier_counts < 3)).any(): pose_status = 'jump-rejected'
            if self.previous_pose is not None:
                world = np.where(good[:, None], world, self.previous_pose)
            else:
                world = np.where(good[:, None], np.nan_to_num(world, nan=0, posinf=0, neginf=0), 0)
            visibility = np.where(good, np.clip(visibility, 0, 1), 0)
            self.previous_pose, self.pose_time = world.copy(), now
            if self.config.enabled:
                world = self.pose_filter.update(world, now)
        else:
            world = None
        image = array(pose_image)
        image_visibility = None
        if image is not None and image.shape == (33, 3):
            image_visibility = np.array([p.visibility for p in pose_image.landmark])
            good = (np.isfinite(image).all(axis=1) & np.isfinite(image_visibility)
                    & (np.abs(image) <= 4).all(axis=1) & (image_visibility >= self.config.min_visibility))
            previous = self.previous_image if self.previous_image is not None else np.zeros_like(image)
            image = np.where(good[:, None], np.nan_to_num(image, nan=0, posinf=0, neginf=0), previous)
            self.previous_image = image.copy()
            image_visibility = np.where(good, np.clip(image_visibility, 0, 1), 0)
            if self.config.enabled:
                image = self.image_filter.update(image, now)
        else:
            image = None
        self.status = dict(left=states[0], right=states[1], pose=pose_status)
        self.last_time = now
        self.last_packet = dict(version=1, pose=packet_points(world, visibility=visibility),
                    imagePose=packet_points(image, aspect, image_visibility),
                    leftHand=hands[0], rightHand=hands[1],
                    overlapActive=overlap_active, overlapManual=overlap_active, adaptiveFiltered=self.config.enabled, trackingStatus=self.status.copy(), observationStatus=observations, sampleTime=float(now))
        return self.last_packet
