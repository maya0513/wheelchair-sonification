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

    // The XR owner keeps hardware actions registered for the lifetime of the OpenXR session.
    public sealed class BorrowedPoseSource : IPoseSource
    {
        private readonly IPoseSource owner;
        public BorrowedPoseSource(IPoseSource owner) { this.owner = owner; }
        public string Kind => owner.Kind;
        public PoseFrame Read(double time) => owner.Read(time);
        public void Dispose() { }
    }

    [Serializable]
    public sealed class RoleBinding
    {
        public string key;
        public int runtimeDeviceId;
    }

    [Serializable] public sealed class WheelCalibration
    {
        public Vector3 axle = Vector3.right;
        public bool ready;
        public float angularSpreadDegrees;
    }
    [Serializable] public sealed class TrackingCalibration
    {
        public int format = 2;
        public RoleBinding[] roles = { new RoleBinding(), new RoleBinding(), new RoleBinding(), new RoleBinding(), new RoleBinding() };
        public WheelCalibration left = new WheelCalibration(), right = new WheelCalibration();
        public bool bodyReady;
        public string createdUtc, invalidReason;
        public bool Complete => bodyReady && left.ready && right.ready && string.IsNullOrEmpty(invalidReason);
        public TrackerPose Pose(PoseFrame frame, TrackerRole role)
        {
            var binding = roles[(int)role]; var pose = frame?.Find(binding.key);
            return pose != null && pose.runtimeDeviceId == binding.runtimeDeviceId ? pose : null;
        }
        public string BindingError(PoseFrame frame, bool requireWrists = false)
        {
            var keys = new HashSet<string>();
            for (int i = 0; i < roles.Length; i++)
            {
                var b = roles[i];
                if (string.IsNullOrEmpty(b.key)) { if (i < 2 && !requireWrists) continue; return "役割が未割当です。"; }
                if (!keys.Add(b.key)) return "同じ機器の役割が重複しています。";
                if (i < 2 && !requireWrists) continue;
                var p = frame?.Find(b.key);
                if (p == null || p.runtimeDeviceId != b.runtimeDeviceId) return "車輪・車体の機器識別が変わりました。";
                if (!p.valid) return "車輪・車体の追跡を確認してください。";
            }
            return null;
        }
        public void Invalidate(string reason) { invalidReason = reason; bodyReady = left.ready = right.ready = false; }
    }
    public static class TrackingGeometry
    {
        public static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w) && Quaternion.Dot(q, q) > .5f;
        public static Quaternion Relative(TrackerPose body, TrackerPose wheel) => Quaternion.Inverse(body.rotation) * wheel.rotation;
        public static Vector3 RotationVector(Quaternion from, Quaternion to)
        {
            var delta = to * Quaternion.Inverse(from); delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180) angle -= 360;
            return float.IsFinite(axis.x) && axis.sqrMagnitude > .5f ? axis.normalized * angle : Vector3.zero;
        }
    }
}
