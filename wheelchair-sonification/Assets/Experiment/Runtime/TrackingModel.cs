using System;
using System.Collections.Generic;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public enum TrackerRole { LeftWrist, RightWrist, LeftWheel, RightWheel, Chassis }

    [Serializable]
    public sealed class TrackerPose
    {
        public string key, product;
        public int runtimeDeviceId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool valid;
        public double acquiredTime;
        [NonSerialized] public double? inputSystemTime;
        public string invalidReason;
    }

    public sealed class PoseFrame
    {
        public double time;
        public readonly List<TrackerPose> poses = new List<TrackerPose>();
        public TrackerPose Find(string key) => poses.Find(p => p.key == key);
    }

    // Input System time is a software-input timestamp, never a sensor measurement timestamp.
    public interface IPoseSource : IDisposable
    {
        string Kind { get; }
        PoseFrame Read(double acquiredTime);
    }

    [Serializable]
    public sealed class RoleBinding
    {
        public string key;
        public int runtimeDeviceId;
    }

    [Serializable]
    public sealed class WheelCalibration
    {
        public Vector3 center, axle = Vector3.right, radialInTracker;
        public float sign = 1;
        public float contactRadius, contactPlane;
        public bool geometryReady, contactReady;
        public float fitResidualM;
    }

    [Serializable]
    public sealed class TrackingCalibration
    {
        public RoleBinding[] roles = { new RoleBinding(), new RoleBinding(), new RoleBinding(), new RoleBinding(), new RoleBinding() };
        public WheelCalibration left = new WheelCalibration(), right = new WheelCalibration();
        public Quaternion chassisReference = Quaternion.identity;
        public Vector3 chassisForwardLocal = Vector3.forward;
        public Vector3 courseStart, courseEnd;
        public bool bodyReady, courseReady;
        public string createdUtc, invalidReason;
        // Initial engineering values, not validated performance. Freeze after separate video validation.
        public float contactEnterM = .04f, contactExitM = .07f, tangentMismatchMps = .40f;
        public float debounceSeconds = .02f, stoppedSpeedMps = .04f, maximumYawDps = 12;
        public float maximumHeadingDegrees = 10, brakingDecelerationMps2 = .4f;
        public double maximumGapSeconds = .10;
        public bool Complete => bodyReady && left.geometryReady && right.geometryReady && left.contactReady &&
            right.contactReady && courseReady && string.IsNullOrEmpty(invalidReason);

        public TrackerPose Pose(PoseFrame frame, TrackerRole role)
        {
            var binding = roles[(int)role];
            var pose = frame.Find(binding.key);
            return pose != null && pose.runtimeDeviceId == binding.runtimeDeviceId ? pose : null;
        }

        public string BindingError(PoseFrame frame)
        {
            var keys = new HashSet<string>();
            foreach (var binding in roles)
            {
                if (string.IsNullOrEmpty(binding.key)) return "役割が未割当です。";
                if (!keys.Add(binding.key)) return "同じ機器を複数の役割に割り当てています。";
                var p = frame.Find(binding.key);
                if (p == null || p.runtimeDeviceId != binding.runtimeDeviceId) return "機器が切断・再接続されています。役割を再確認してください。";
                if (!p.valid) return "5台すべての位置・姿勢が有効になるまで待ってください。";
            }
            return null;
        }

        public Quaternion BodyRotation(TrackerPose body) => body.rotation * Quaternion.Inverse(chassisReference);
        public Vector3 ToBody(TrackerPose body, Vector3 position) => Quaternion.Inverse(BodyRotation(body)) * (position - body.position);
        public void Invalidate(string reason) { invalidReason = reason; }
    }

    public static class TrackingGeometry
    {
        public static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) &&
            float.IsFinite(q.w) && Quaternion.Dot(q, q) > .5f;

        public static float WheelPhase(TrackingCalibration calibration, TrackerPose body, TrackerPose wheel, WheelCalibration geometry)
        {
            var rotation = Quaternion.Inverse(calibration.BodyRotation(body)) * wheel.rotation;
            var radial = Vector3.ProjectOnPlane(rotation * geometry.radialInTracker, geometry.axle).normalized;
            var up = Vector3.ProjectOnPlane(Vector3.up, geometry.axle).normalized;
            return Vector3.SignedAngle(up, radial, geometry.axle) * geometry.sign;
        }

        public static float ContactDistance(TrackingCalibration calibration, TrackerPose body, TrackerPose hand, WheelCalibration wheel)
        {
            var offset = calibration.ToBody(body, hand.position) - wheel.center;
            float axial = Vector3.Dot(offset, wheel.axle);
            float radius = Vector3.ProjectOnPlane(offset, wheel.axle).magnitude;
            return Mathf.Sqrt(Mathf.Pow(axial - wheel.contactPlane, 2) + Mathf.Pow(radius - wheel.contactRadius, 2));
        }
    }
}
