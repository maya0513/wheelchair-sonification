using System;
using UnityEngine;
namespace WheelchairSonification.Runtime
{
    // Explicit fixture. Never a fallback for hardware failure.
    public sealed class SyntheticPoseSource : IPoseSource
    {
        public string Kind => "synthetic_pose_fixture";
        public double LeftRps = .4, RightRps = .6;
        public bool Moving = true;
        public PoseFrame Read(double time)
        {
            var frame = new PoseFrame { time = time }; var heading = Quaternion.Euler(0, (float)(time * 8), 0);
            for (int i = 0; i < 5; i++)
            {
                double rps = i == 2 ? LeftRps : RightRps;
                var axis = Quaternion.Euler(0, 0, i == 2 ? -12 : 12) * Vector3.right;
                frame.poses.Add(new TrackerPose { key = "synthetic-" + i, product = "SYNTHETIC ONLY", runtimeDeviceId = 900 + i,
                    acquiredTime = time, inputSystemTime = time, valid = true,
                    position = new Vector3((float)(Math.Sin(time * .1)), .4f, (float)(Math.Cos(time * .1))) + heading * new Vector3(i % 2 == 0 ? -.3f : .3f, 0, 0),
                    rotation = heading * (i == 2 || i == 3 ? Quaternion.AngleAxis((float)(time * rps * 360 * (Moving ? 1 : 0)), axis) : Quaternion.identity) });
            }
            return frame;
        }
        public static TrackingCalibration Calibration()
        {
            var c = new TrackingCalibration { bodyReady = true, createdUtc = DateTime.UtcNow.ToString("O") };
            for (int i = 0; i < 5; i++) { c.roles[i].key = "synthetic-" + i; c.roles[i].runtimeDeviceId = 900 + i; }
            c.left = new WheelCalibration { ready = true, axle = Quaternion.Euler(0, 0, -12) * Vector3.right };
            c.right = new WheelCalibration { ready = true, axle = Quaternion.Euler(0, 0, 12) * Vector3.right }; return c;
        }
        public void Dispose() { }
    }
}
