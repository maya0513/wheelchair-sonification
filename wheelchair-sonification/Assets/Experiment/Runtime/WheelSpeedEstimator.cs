using WheelchairSonification.Core;
using UnityEngine;
namespace WheelchairSonification.Runtime
{
    public struct WheelSpeedSample { public double leftRps, rightRps; public bool valid, fresh; public string reason; }
    public sealed class WheelSpeedEstimator
    {
        sealed class WheelState { public Quaternion rotation; public double time, acquired; public bool seeded, hasSpeed; public double speed; }
        readonly WheelState left = new WheelState(), right = new WheelState();
        readonly TrackingCalibration calibration;
        readonly double maximumGap;
        public WheelSpeedEstimator(TrackingCalibration calibration, double maximumGap = .10) { this.calibration = calibration; this.maximumGap = maximumGap; }
        public void Reset() { left.seeded = right.seeded = left.hasSpeed = right.hasSpeed = false; left.speed = right.speed = 0; }
        public WheelSpeedSample Update(PoseFrame frame)
        {
            if (!calibration.Complete) { Reset(); return new WheelSpeedSample { reason = "calibration_invalid" }; }
            // Wrist tracking never participates in this gate.
            foreach (var role in new[] { TrackerRole.Chassis, TrackerRole.LeftWheel, TrackerRole.RightWheel })
            {
                var b = calibration.roles[(int)role]; var pose = frame?.Find(b.key);
                if (pose != null && pose.runtimeDeviceId != b.runtimeDeviceId) calibration.Invalidate("critical_device_reconnected");
                if (pose == null || pose.runtimeDeviceId != b.runtimeDeviceId || !pose.valid || !TrackingGeometry.Finite(pose.rotation))
                { Reset(); return new WheelSpeedSample { reason = "critical_pose_missing_or_reconnected" }; }
            }
            var body = calibration.Pose(frame, TrackerRole.Chassis);
            bool lf, rf;
            bool lv = Calculate(left, body, calibration.Pose(frame, TrackerRole.LeftWheel), calibration.left, frame.time, out lf);
            bool rv = Calculate(right, body, calibration.Pose(frame, TrackerRole.RightWheel), calibration.right, frame.time, out rf);
            return new WheelSpeedSample { valid = lv && rv, fresh = lf || rf, leftRps = left.speed, rightRps = right.speed, reason = lv && rv ? "" : "new_difference_required" };
        }
        bool Calculate(WheelState state, TrackerPose body, TrackerPose wheel, WheelCalibration axis, double acquired, out bool fresh)
        {
            fresh = false;
            double t = wheel.inputSystemTime ?? acquired;
            var rotation = TrackingGeometry.Relative(body, wheel);
            if (state.seeded && t == state.time)
            {
                if (acquired - state.acquired > maximumGap) { state.seeded = state.hasSpeed = false; return false; }
                // Only hold a velocity obtained from two distinct timestamps, never the initial placeholder zero.
                return state.hasSpeed;
            }
            double dt = t - state.time;
            bool valid = state.seeded && dt > 0 && dt <= maximumGap;
            if (valid) { state.speed = Vector3.Dot(TrackingGeometry.RotationVector(state.rotation, rotation), axis.axle) / 360.0 / dt; fresh = true; }
            state.rotation = rotation; state.time = t; state.acquired = acquired; state.seeded = true; state.hasSpeed = valid;
            if (!valid) state.speed = 0;
            return valid;
        }
    }
}
