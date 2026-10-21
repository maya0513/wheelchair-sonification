using System;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    // Explicit technical fixture. Never chosen automatically after a real-device failure.
    public sealed class SyntheticPoseSource : IPoseSource
    {
        public string Kind => "synthetic_pose_fixture";
        public bool Moving;
        private double started = double.NaN, stoppedTime;
        private Vector3 heldPosition;
        private Quaternion heldRotation = Quaternion.identity;
        public PoseFrame Read(double acquiredTime)
        {
            if (Moving && double.IsNaN(started)) started = acquiredTime - stoppedTime;
            double t = Moving ? acquiredTime - started : stoppedTime;
            Vector3 body = heldPosition; Quaternion heading = heldRotation;
            if (Moving)
            {
                float leg = Mathf.Repeat((float)t, 24);
                body = new Vector3(0, 0, leg <= 12 ? leg * (10f / 12) : 20 - leg * (10f / 12));
                heading = Quaternion.Euler(0, leg <= 12 ? 0 : 180, 0);
                heldPosition = body; heldRotation = heading; stoppedTime = t;
            }
            else started = double.NaN;
            float wheelAngle = (float)t * 120;
            Quaternion spin = Quaternion.AngleAxis(wheelAngle, Vector3.right);
            var frame = new PoseFrame { time = acquiredTime };
            float phase = Mathf.Repeat((float)t, 1.2f);
            Vector3 radial = spin * Vector3.up * .3f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 local = i == 4 ? Vector3.zero : new Vector3(i % 2 == 0 ? -.32f : .32f, .35f, 0);
                if (i < 2) local += radial + (phase < .6f ? Vector3.zero : Vector3.right * .15f);
                frame.poses.Add(new TrackerPose { key = "synthetic-" + i, product = "SYNTHETIC ONLY", runtimeDeviceId = 900 + i,
                    acquiredTime = acquiredTime, valid = true, position = body + heading * local,
                    rotation = i == 2 || i == 3 ? heading * spin : heading });
            }
            return frame;
        }
        public static TrackingCalibration Calibration()
        {
            var c = new TrackingCalibration { bodyReady = true, courseReady = true, courseEnd = Vector3.forward * 10, createdUtc = DateTime.UtcNow.ToString("O") };
            for (int i = 0; i < 5; i++) { c.roles[i].key = "synthetic-" + i; c.roles[i].runtimeDeviceId = 900 + i; }
            c.left = Wheel(-.32f); c.right = Wheel(.32f); return c;
        }
        private static WheelCalibration Wheel(float x) => new WheelCalibration { center = new Vector3(x, .35f, 0), axle = Vector3.right,
            radialInTracker = Vector3.up, sign = 1, contactRadius = .3f, contactPlane = 0, geometryReady = true, contactReady = true };
        public void Dispose() { }
    }
}
