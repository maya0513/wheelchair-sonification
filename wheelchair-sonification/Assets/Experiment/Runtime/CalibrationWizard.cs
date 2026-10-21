using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public sealed class CalibrationWizard
    {
        public TrackingCalibration Calibration { get; private set; } = new TrackingCalibration();
        public int Step { get; private set; }
        public int HandSamples { get; private set; }
        public bool Collecting { get; private set; }
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Quaternion> rotations = new List<Quaternion>();
        private readonly List<Vector2> contacts = new List<Vector2>();
        private Vector3 bodyAtStart;
        private bool collectionBroken;
        public string Instruction => Step switch
        {
            0 => "車体を水平・正面向きで静止させ、記録。",
            1 => Collecting ? "左車輪をゆっくり前進方向へ\n60°以上回してから、確定。" : "車体を動かさず、左車輪の\n回転収録を開始します。",
            2 => Collecting ? "右車輪をゆっくり前進方向へ\n60°以上回してから、確定。" : "車体を動かさず、右車輪の\n回転収録を開始します。",
            3 => "車体を直進コースの開始線に置き、\n静止して記録。",
            4 => "同じ向きのまま終了線（10 m）へ移動。\n静止して記録。",
            5 => $"左手をリムの異なる3位置に添え、\n各位置で記録（{HandSamples}/3）。",
            6 => $"右手をリムの異なる3位置に添え、\n各位置で記録（{HandSamples}/3）。",
            _ => "校正完了。再装着・会場変更時はやり直す。"
        };
        public string ActionLabel => Collecting ? "回転を止めて確定" : Step == 1 || Step == 2 ? "回転の記録を開始" : "この位置を記録";

        public void Reset()
        {
            var bindings = Calibration.roles;
            Calibration = new TrackingCalibration { roles = bindings };
            Step = 0; HandSamples = 0; Collecting = false;
            positions.Clear(); rotations.Clear(); contacts.Clear();
        }

        public void UseSyntheticCalibration(TrackingCalibration calibration)
        {
            if (calibration.roles.Any(r => r.key == null || !r.key.StartsWith("synthetic-"))) throw new ArgumentException("合成入力専用です。");
            Calibration = calibration; Step = 7; Collecting = false; HandSamples = 0;
        }

        public void BeginParticipant()
        {
            Calibration.left.contactReady = Calibration.right.contactReady = false;
            contacts.Clear(); HandSamples = 0; Collecting = false;
            if (Calibration.bodyReady && Calibration.courseReady && Calibration.left.geometryReady &&
                Calibration.right.geometryReady && string.IsNullOrEmpty(Calibration.invalidReason)) Step = 5;
            else Reset();
        }

        public void Assign(TrackerRole role, string key, int runtimeId)
        {
            var binding = Calibration.roles[(int)role];
            if (binding.key == key && binding.runtimeDeviceId == runtimeId) return;
            binding.key = key; binding.runtimeDeviceId = runtimeId;
            if (role == TrackerRole.LeftWrist || role == TrackerRole.RightWrist) BeginParticipant();
            else Reset();
        }

        public void Observe(PoseFrame frame)
        {
            if (!Collecting) return;
            var body = Calibration.Pose(frame, TrackerRole.Chassis);
            var wheel = Calibration.Pose(frame, Step == 1 ? TrackerRole.LeftWheel : TrackerRole.RightWheel);
            if (body == null || wheel == null || !body.valid || !wheel.valid || Vector3.Distance(body.position, bodyAtStart) > .02f)
            { collectionBroken = true; return; }
            positions.Add(Calibration.ToBody(body, wheel.position));
            rotations.Add(Quaternion.Inverse(Calibration.BodyRotation(body)) * wheel.rotation);
        }

        public string Capture(PoseFrame frame)
        {
            string error = Calibration.BindingError(frame);
            if (error != null) return error;
            var body = Calibration.Pose(frame, TrackerRole.Chassis);
            if (Step == 0)
            {
                // An arbitrary rigid mounting is allowed. Course calibration establishes chair heading later.
                Calibration.chassisReference = body.rotation;
                Calibration.bodyReady = true;
                Calibration.createdUtc = DateTime.UtcNow.ToString("O");
                Step++; return null;
            }
            if (Step == 1 || Step == 2)
            {
                if (!Collecting)
                {
                    positions.Clear(); rotations.Clear(); bodyAtStart = body.position; collectionBroken = false;
                    Collecting = true; Observe(frame); return null;
                }
                Collecting = false;
                if (collectionBroken) return "収録中に車体が移動、または追跡を失いました。再収録してください。";
                try
                {
                    var fitted = FitWheel(positions, rotations);
                    if (Step == 1) Calibration.left = fitted; else Calibration.right = fitted;
                    Step++; return null;
                }
                catch (ArgumentException e) { return e.Message; }
            }
            if (Step == 5 || Step == 6)
            {
                var hand = Calibration.Pose(frame, Step == 5 ? TrackerRole.LeftWrist : TrackerRole.RightWrist);
                var wheel = Step == 5 ? Calibration.left : Calibration.right;
                Vector3 offset = Calibration.ToBody(body, hand.position) - wheel.center;
                contacts.Add(new Vector2(Vector3.ProjectOnPlane(offset, wheel.axle).magnitude, Vector3.Dot(offset, wheel.axle)));
                HandSamples++;
                if (HandSamples < 3) return null;
                wheel.contactRadius = contacts.Average(p => p.x);
                wheel.contactPlane = contacts.Average(p => p.y);
                if (wheel.contactRadius < .10f || wheel.contactRadius > .65f || contacts.Any(p => Mathf.Abs(p.x - wheel.contactRadius) > .07f))
                { contacts.Clear(); HandSamples = 0; return "接触基準のばらつきが大きいか、車輪中心が不適切です。校正を確認してください。"; }
                wheel.contactReady = true;
                contacts.Clear(); HandSamples = 0; Step++; return null;
            }
            if (Step == 3) { Calibration.courseStart = body.position; Step++; return null; }
            if (Step == 4)
            {
                Vector3 delta = Vector3.ProjectOnPlane(body.position - Calibration.courseStart, Vector3.up);
                if (delta.magnitude < 9.5f || delta.magnitude > 10.5f) return "開始・終了位置の距離が9.5–10.5 mに入りません。設営と追跡を確認してください。";
                if (Quaternion.Angle(Calibration.BodyRotation(body), Quaternion.identity) > 10) return "校正開始時と同じ向きで終了線に置いてください。";
                Calibration.chassisForwardLocal = Quaternion.Inverse(Calibration.BodyRotation(body)) * delta.normalized;
                Calibration.courseEnd = body.position; Calibration.courseReady = true; Step++; return null;
            }
            return null;
        }

        public static WheelCalibration FitWheel(IReadOnlyList<Vector3> points, IReadOnlyList<Quaternion> rotations)
        {
            if (points.Count < 6 || points.Count != rotations.Count) throw new ArgumentException("回転データが不足しています。ゆっくり大きく回してください。");
            Vector3 axis = Vector3.zero;
            Vector3 firstAxis = Vector3.zero;
            float span = 0;
            var reference = rotations[0];
            foreach (var q in rotations)
            {
                (q * Quaternion.Inverse(reference)).ToAngleAxis(out float a, out Vector3 n);
                if (a > 180) { a = 360 - a; n = -n; }
                if (a < 5) continue;
                if (firstAxis == Vector3.zero) firstAxis = n;
                if (Vector3.Dot(n, firstAxis) < 0) n = -n;
                axis += n; span = Mathf.Max(span, a);
            }
            if (span < 60 || axis.sqrMagnitude < .01f) throw new ArgumentException("60°以上の車輪回転が必要です。正逆に往復させず前進方向へ回してください。");
            axis.Normalize();
            if (Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .7072f) throw new ArgumentException("回転軸が大きく上下を向いています。固定と回転収録を確認してください。");
            Vector3 mean = Vector3.zero;
            foreach (var p in points) mean += p;
            mean /= points.Count;
            Vector3 u = Vector3.ProjectOnPlane(Vector3.up, axis).normalized, v = Vector3.Cross(axis, u);
            var matrix = new double[3, 4];
            float spread = 0;
            foreach (var p in points)
            {
                var d = p - mean;
                double x = Vector3.Dot(d, u), y = Vector3.Dot(d, v), z = x * x + y * y;
                double[] row = { 2 * x, 2 * y, 1 };
                for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) matrix[i, j] += row[i] * row[j]; matrix[i, 3] += row[i] * z; }
                spread = Mathf.Max(spread, d.magnitude);
            }
            Vector3 center = mean;
            if (spread > .01f)
            {
                for (int i = 0; i < 3; i++)
                {
                    int pivot = i;
                    for (int j = i + 1; j < 3; j++) if (Math.Abs(matrix[j, i]) > Math.Abs(matrix[pivot, i])) pivot = j;
                    if (Math.Abs(matrix[pivot, i]) < 1e-9) throw new ArgumentException("車輪中心を推定できません。回転範囲と追跡を確認してください。");
                    for (int j = i; j < 4; j++) (matrix[i, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[i, j]);
                    double scale = matrix[i, i];
                    for (int j = i; j < 4; j++) matrix[i, j] /= scale;
                    for (int k = 0; k < 3; k++) if (k != i) { double f = matrix[k, i]; for (int j = i; j < 4; j++) matrix[k, j] -= f * matrix[i, j]; }
                }
                center += u * (float)matrix[0, 3] + v * (float)matrix[1, 3];
            }
            float radius = points.Average(p => Vector3.ProjectOnPlane(p - center, axis).magnitude);
            float residual = points.Average(p => Mathf.Abs(Vector3.ProjectOnPlane(p - center, axis).magnitude - radius));
            if (!TrackingGeometry.Finite(center) || residual > .01f) throw new ArgumentException("車輪の円軌道誤差が1 cmを超えます。収録をやり直してください。");
            Vector3 radial = Quaternion.Inverse(reference) * u;
            float direction = 0;
            for (int i = 1; i < rotations.Count; i++)
                direction += Vector3.SignedAngle(rotations[i - 1] * radial, rotations[i] * radial, axis);
            return new WheelCalibration { center = center, axle = axis, radialInTracker = radial,
                sign = direction >= 0 ? 1 : -1, geometryReady = true, fitResidualM = residual };
        }
    }
}
