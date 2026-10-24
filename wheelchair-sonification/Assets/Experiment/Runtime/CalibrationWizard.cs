using System;
using System.Collections.Generic;
using UnityEngine;
namespace WheelchairSonification.Runtime
{
    public sealed class CalibrationWizard
    {
        public TrackingCalibration Calibration { get; private set; } = new TrackingCalibration();
        public int Step { get; private set; }
        readonly List<Quaternion> rotations = new List<Quaternion>();
        public string Instruction => Step == 0 ? "役割を確認し、静止して車体基準を取得" : Step == 1 ? "左車輪を前進方向へゆっくり回す → 校正確定" : Step == 2 ? "右車輪を前進方向へゆっくり回す → 校正確定" : "左右軸の校正完了。既知角と方向を確認";
        public void Reset() { Calibration.Invalidate("operator_reset"); Calibration = new TrackingCalibration { roles = Calibration.roles }; Step = 0; rotations.Clear(); }
        public void Bind(TrackerRole role, TrackerPose pose)
        {
            if (pose == null) throw new ArgumentException("機器を選択してください。");
            for (int i = 0; i < Calibration.roles.Length; i++) if (i != (int)role && Calibration.roles[i].key == pose.key) throw new ArgumentException("同じ機器を重複して割り当てられません。");
            Calibration.roles[(int)role] = new RoleBinding { key = pose.key, runtimeDeviceId = pose.runtimeDeviceId };
            if ((int)role >= 2) { Calibration.Invalidate("binding_changed"); Step = 0; rotations.Clear(); }
        }
        public void Observe(PoseFrame frame)
        {
            if (Step != 1 && Step != 2) return;
            var b = Calibration.Pose(frame, TrackerRole.Chassis); var w = Calibration.Pose(frame, Step == 1 ? TrackerRole.LeftWheel : TrackerRole.RightWheel);
            if (b?.valid != true || w?.valid != true) { rotations.Clear(); return; }
            var q = TrackingGeometry.Relative(b, w);
            if (rotations.Count == 0 || Quaternion.Angle(rotations[rotations.Count - 1], q) > .5f) rotations.Add(q);
        }
        public void Capture(PoseFrame frame)
        {
            string error = Calibration.BindingError(frame); if (error != null) throw new InvalidOperationException(error);
            if (Step == 0) { Calibration.invalidReason = null; Calibration.bodyReady = true; Step = 1; rotations.Clear(); Observe(frame); return; }
            if (Step > 2) throw new InvalidOperationException("校正は完了しています。取り直す場合はリセットしてください。");
            Observe(frame);
            Vector3 sum = Vector3.zero; float total = 0;
            for (int i = 1; i < rotations.Count; i++) { var v = TrackingGeometry.RotationVector(rotations[i-1], rotations[i]); sum += v; total += v.magnitude; }
            if (sum.magnitude < 45 || sum.magnitude < total * .8f) throw new InvalidOperationException("同じ前進方向へ45°以上ゆっくり回してください。往復や軸の変化を避けます。");
            var wheel = Step == 1 ? Calibration.left : Calibration.right;
            wheel.axle = sum.normalized; wheel.ready = true; wheel.angularSpreadDegrees = total;
            Step++; rotations.Clear(); Calibration.createdUtc = DateTime.UtcNow.ToString("O");
        }
        public void UseSynthetic(TrackingCalibration calibration) { Calibration = calibration; Step = 3; rotations.Clear(); }
    }
}
