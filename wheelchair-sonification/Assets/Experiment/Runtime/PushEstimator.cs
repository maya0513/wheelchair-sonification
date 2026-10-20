using System;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    // Causal, conservative contact CANDIDATES. No force sensing. All thresholds are logged.
    public sealed class PushEstimator : IExperimentInput
    {
        private readonly TrackingCalibration calibration;
        private readonly bool right;
        private double previousTime = double.NaN, eligibleSince = double.NaN;
        private Vector3 previousBody, previousHand;
        private Quaternion previousBodyRotation;
        private float previousPhase, unwrapped, contactAngle, previousSpeed;
        private bool open;
        private ExperimentContext previousContext;
        public event Action<ExperimentInput> Received;
        public float SpeedMps { get; private set; }
        public float CoursePositionM { get; private set; }
        public MotionState Motion { get; private set; } = MotionState.Stopped;
        public bool TrackingValid { get; private set; }
        public string InvalidReason { get; private set; }
        public bool ContactOpen => open;

        public PushEstimator(TrackingCalibration calibration, bool majorRight)
        { this.calibration = calibration; right = majorRight; }

        public void Observe(PoseFrame frame, StudyPhase phase, StudyTask task)
        {
            var body = calibration.Pose(frame, TrackerRole.Chassis);
            var hand = calibration.Pose(frame, right ? TrackerRole.RightWrist : TrackerRole.LeftWrist);
            var wheel = calibration.Pose(frame, right ? TrackerRole.RightWheel : TrackerRole.LeftWheel);
            var geometry = right ? calibration.right : calibration.left;
            double dt = frame.time - previousTime;
            bool valid = !double.IsNaN(frame.time) && !double.IsInfinity(frame.time) && calibration.Complete && calibration.BindingError(frame) == null;
            if (valid) valid = TrackingGeometry.Finite(body.position) && TrackingGeometry.Finite(hand.position) &&
                TrackingGeometry.Finite(wheel.rotation) && TrackingGeometry.Finite(body.rotation);
            if (!valid || (!double.IsNaN(previousTime) && (dt <= 0 || dt > calibration.maximumGapSeconds)))
            {
                InvalidReason = !calibration.Complete ? "calibration_invalid" : !valid ? "tracking_invalid_or_reconnected" : "timestamp_gap";
                SetContext(frame.time, phase, task, MotionState.Stopped, false, false);
                if (open) { Received?.Invoke(ExperimentInput.Release(frame.time, double.NaN)); open = false; }
                previousTime = double.NaN; eligibleSince = double.NaN; TrackingValid = false; SpeedMps = 0; Motion = MotionState.Stopped;
                return;
            }
            TrackingValid = true; InvalidReason = null;
            var rotation = calibration.BodyRotation(body);
            var localHand = calibration.ToBody(body, hand.position);
            float currentPhase = TrackingGeometry.WheelPhase(calibration, body, wheel, geometry);
            Vector3 forward = Vector3.ProjectOnPlane(calibration.courseEnd - calibration.courseStart, Vector3.up).normalized;
            CoursePositionM = Vector3.Dot(body.position - calibration.courseStart, forward);
            if (double.IsNaN(previousTime))
            {
                previousTime = frame.time; previousBody = body.position; previousHand = localHand;
                previousPhase = currentPhase; previousBodyRotation = rotation; previousSpeed = 0;
                SpeedMps = 0; Motion = MotionState.Stopped;
                SetContext(frame.time, phase, task, MotionState.Stopped, false, true);
                return;
            }
            float delta = Mathf.DeltaAngle(previousPhase, currentPhase);
            if (Mathf.Abs(delta) > 720 * dt + 5)
            {
                SetContext(frame.time, phase, task, MotionState.Stopped, false, false);
                if (open) { Received?.Invoke(ExperimentInput.Release(frame.time, double.NaN)); open = false; }
                previousTime = double.NaN; eligibleSince = double.NaN; TrackingValid = false; InvalidReason = "rotation_jump";
                SpeedMps = 0; Motion = MotionState.Stopped;
                return;
            }
            unwrapped += delta;
            SpeedMps = Vector3.ProjectOnPlane(body.position - previousBody, Vector3.up).magnitude / (float)dt;
            float yawRate = Quaternion.Angle(previousBodyRotation, rotation) / (float)dt;
            float heading = Vector3.Angle(rotation * calibration.chassisForwardLocal, forward);
            heading = Mathf.Min(heading, 180 - heading); // Both directions along the course are valid.
            float deceleration = (SpeedMps - previousSpeed) / (float)dt;
            Motion = SpeedMps < calibration.stoppedSpeedMps ? MotionState.Stopped :
                yawRate > calibration.maximumYawDps || heading > calibration.maximumHeadingDegrees ? MotionState.Turning :
                deceleration < -calibration.brakingDecelerationMps2 ? MotionState.Braking : MotionState.Straight;
            bool central = CoursePositionM >= 2 && CoursePositionM <= 8;
            SetContext(frame.time, phase, task, Motion, central, true);

            Vector3 radial = Vector3.ProjectOnPlane(localHand - geometry.center, geometry.axle);
            Vector3 expectedVelocity = Vector3.Cross(geometry.axle, radial) * (delta / geometry.sign * Mathf.Deg2Rad / (float)dt);
            Vector3 measuredVelocity = (localHand - previousHand) / (float)dt;
            float mismatch = (expectedVelocity - Vector3.ProjectOnPlane(measuredVelocity, geometry.axle)).magnitude;
            float distance = TrackingGeometry.ContactDistance(calibration, body, hand, geometry);
            bool near = distance <= calibration.contactEnterM && mismatch <= calibration.tangentMismatchMps && delta > 0;
            bool far = distance >= calibration.contactExitM || mismatch > calibration.tangentMismatchMps || delta < 0;
            if (!open)
            {
                if (near)
                {
                    if (double.IsNaN(eligibleSince)) { eligibleSince = frame.time; contactAngle = unwrapped - delta; }
                    if (frame.time - eligibleSince >= calibration.debounceSeconds)
                    { open = true; eligibleSince = double.NaN; Received?.Invoke(ExperimentInput.Contact(frame.time)); }
                }
                else eligibleSince = double.NaN;
            }
            else if (far)
            {
                // The previous sample was the last contact candidate; do not include the first free-hand sample.
                double angle = unwrapped - delta - contactAngle;
                open = false; Received?.Invoke(ExperimentInput.Release(frame.time, angle));
            }
            previousTime = frame.time; previousBody = body.position; previousHand = localHand;
            previousPhase = currentPhase; previousBodyRotation = rotation; previousSpeed = SpeedMps;
        }

        private void SetContext(double time, StudyPhase phase, StudyTask task, MotionState motion, bool central, bool tracked)
        {
            var context = new ExperimentContext(phase, task, motion, central, tracked);
            if (context.Phase == previousContext.Phase && context.Task == previousContext.Task && context.Motion == previousContext.Motion &&
                context.InCentralArea == previousContext.InCentralArea && context.TrackingValid == previousContext.TrackingValid) return;
            previousContext = context;
            Received?.Invoke(ExperimentInput.ChangeContext(time, context));
        }
    }
}
