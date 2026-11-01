using System;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class DisplayPlacement
    {
        public float distanceMeters = 1.4f,
            horizontalDegrees = 5,
            downwardDegrees = 12,
            barWidthDegrees = 1.2f,
            barHeightDegrees = 8;
        public float fillOpacity = .7f,
            trackOpacity = .2f,
            labelOpacity = .9f;
        public Vector3 bodyLocalPosition;
        public Quaternion bodyLocalRotation = Quaternion.identity;
        public string bodyKey;
        public int bodyRuntimeId;
        public bool aligned;
    }

    // Coordinates are Unity tracking-space metres, independent of the tracker's mounting rotation.
    public sealed class FeedbackDisplayAnchor
    {
        public DisplayPlacement Placement { get; } = new DisplayPlacement();
        TrackingCalibration alignedCalibration;

        public void Invalidate()
        {
            Placement.aligned = false;
            alignedCalibration = null;
        }

        public bool IsValid(PoseFrame frame, TrackingCalibration calibration)
        {
            var p = calibration?.Pose(frame, TrackerRole.Chassis);
            return Placement.aligned
                && ReferenceEquals(calibration, alignedCalibration)
                && calibration.Complete
                && p != null
                && p.valid
                && p.key == Placement.bodyKey
                && p.runtimeDeviceId == Placement.bodyRuntimeId
                && TrackingGeometry.Finite(p.position)
                && TrackingGeometry.Finite(p.rotation);
        }

        public void Align(
            PoseFrame frame,
            TrackingCalibration calibration,
            Vector3 headPosition,
            Quaternion headRotation
        )
        {
            var body = calibration?.Pose(frame, TrackerRole.Chassis);
            if (
                calibration?.Complete != true
                || body?.valid != true
                || !TrackingGeometry.Finite(headPosition)
                || !TrackingGeometry.Finite(headRotation)
            )
                throw new InvalidOperationException(
                    "停止して、車体・ゴーグルの追跡と校正を確認してください。"
                );
            var forward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < .1f)
                throw new InvalidOperationException("正面を向いて表示位置を合わせてください。");
            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            var position =
                headPosition
                + rotation
                    * new Vector3(
                        0,
                        -Mathf.Tan(Placement.downwardDegrees * Mathf.Deg2Rad)
                            * Placement.distanceMeters,
                        Placement.distanceMeters
                    );
            Placement.bodyLocalPosition =
                Quaternion.Inverse(body.rotation) * (position - body.position);
            Placement.bodyLocalRotation = Quaternion.Inverse(body.rotation) * rotation;
            Placement.bodyKey = body.key;
            Placement.bodyRuntimeId = body.runtimeDeviceId;
            alignedCalibration = calibration;
            Placement.aligned = true;
        }

        public Pose Pose(PoseFrame frame, TrackingCalibration calibration)
        {
            if (!IsValid(frame, calibration))
                throw new InvalidOperationException("表示位置を合わせてください。");
            var body = calibration.Pose(frame, TrackerRole.Chassis);
            return new Pose(
                body.position + body.rotation * Placement.bodyLocalPosition,
                body.rotation * Placement.bodyLocalRotation
            );
        }
    }

    public sealed class DisplayObservation
    {
        public bool headValid,
            anchorValid,
            visible;
        public Vector3 headPosition,
            originPosition,
            displayPosition;
        public Quaternion headRotation = Quaternion.identity,
            originRotation = Quaternion.identity,
            displayRotation = Quaternion.identity;
        public DisplayPlacement placement = new DisplayPlacement();
        public double left,
            right;
    }
}
