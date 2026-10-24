using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    [Serializable] public sealed class ResearchSessionSettings
    {
        public int format = 2, repetition;
        public string participant_id, visit_id, trial_id, phase, task, group, input_source, created_utc;
        public string application_version, unity_version, source_commit;
        public string target_device = "VIVE Focus Vision", tracker_product = "VIVE Ultimate Tracker";
        public string vive_openxr_version = "2.5.1", unity_openxr_version = "1.18.0";
        public string timestamp_clock = "Unity software acquisition / Input System; sensor timestamps unavailable";
        public string measurement_validation = "physical_device_accuracy_and_output_pending";
        public string formal_task_time = "PC operator.json, linked by trial_id";
        public FeedbackSettings feedback = new FeedbackSettings();
        public float digital_volume;
    }
    public sealed class ResearchSessionLog : IDisposable
    {
        readonly StreamWriter poses, speeds, events, diagnostics;
        public string DirectoryPath { get; }
        bool closed;
        public ResearchSessionLog(string root, ResearchSessionSettings settings, TrackingCalibration calibration)
        {
            FileStorage.EnsureReserve(root);
            DirectoryPath = Path.Combine(root, settings.trial_id);
            if (Directory.Exists(DirectoryPath)) throw new IOException("同じ試行IDが既に存在します。");
            Directory.CreateDirectory(DirectoryPath);
            try
            {
                File.WriteAllText(Path.Combine(DirectoryPath, "session.json"), JsonUtility.ToJson(settings, true));
                File.WriteAllText(Path.Combine(DirectoryPath, "calibration.json"), JsonUtility.ToJson(calibration, true));
                poses = Writer("poses.csv", "acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason");
                speeds = Writer("speeds.csv", "software_time_s,valid,fresh,left_raw_rps,right_raw_rps,left_filtered_rps,right_filtered_rps,left_output,right_output,feedback_enabled,reason");
                events = Writer("events.csv", "software_time_s,event,reason");
                diagnostics = Writer("diagnostics.csv", "software_time_s,frame_interval_s");
            }
            catch { Dispose(); throw; }
        }
        StreamWriter Writer(string name, string header)
        {
            var w = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false), 16384);
            w.WriteLine(header); w.Flush(); return w;
        }
        public void Poses(PoseFrame frame, TrackingCalibration calibration)
        {
            foreach (TrackerRole role in Enum.GetValues(typeof(TrackerRole)))
            {
                var binding = calibration.roles[(int)role]; var p = frame.Find(binding.key);
                bool identityMatches = p != null && p.runtimeDeviceId == binding.runtimeDeviceId;
                bool valid = identityMatches && p.valid && TrackingGeometry.Finite(p.position) && TrackingGeometry.Finite(p.rotation);
                poses.WriteLine(string.Join(",", new[] { N(frame.time), N(p?.inputSystemTime), role.ToString(), E(binding.key), p?.runtimeDeviceId.ToString(CultureInfo.InvariantCulture) ?? "", valid ? "true" : "false",
                    valid ? N(p.position.x) : "", valid ? N(p.position.y) : "", valid ? N(p.position.z) : "", valid ? N(p.rotation.x) : "", valid ? N(p.rotation.y) : "", valid ? N(p.rotation.z) : "", valid ? N(p.rotation.w) : "", E(p == null ? "missing_pose" : !identityMatches ? "device_identity_changed" : valid ? "" : p.invalidReason ?? "invalid_pose") }));
            }
        }
        public void Speed(double time, WheelSpeedSample s, BilateralFeedback f) => speeds.WriteLine(string.Join(",", new[] { N(time), s.valid.ToString(), s.fresh.ToString(), s.valid ? N(s.leftRps) : "", s.valid ? N(s.rightRps) : "", f.valid ? N(f.left.filteredRps) : "", f.valid ? N(f.right.filteredRps) : "", N(f.left.normalized), N(f.right.normalized), f.enabled.ToString(), E(s.reason) }));
        public void Event(double time, string name, string reason = "") => events.WriteLine(N(time) + "," + E(name) + "," + E(reason));
        public void Diagnostic(double time, double interval) => diagnostics.WriteLine(N(time) + "," + N(interval));
        public void Flush() { poses.Flush(); speeds.Flush(); events.Flush(); diagnostics.Flush(); }
        public void Complete(double time, bool trialComplete, string reason)
        {
            Event(time, trialComplete ? "record_closed" : "record_interrupted", reason); Flush(); Dispose();
            File.WriteAllText(Path.Combine(DirectoryPath, "completed.json"), "{\"format\":2,\"files_closed\":true,\"trial_complete\":" + (trialComplete ? "true" : "false") + ",\"software_time_s\":" + N(time) + "}");
        }
        public void Dispose() { if (closed) return; closed = true; poses?.Dispose(); speeds?.Dispose(); events?.Dispose(); diagnostics?.Dispose(); }
        internal static string N(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        internal static string E(string value) => string.IsNullOrEmpty(value) ? "" : value.IndexOfAny(new[] { ',', '\"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
    // For preview/replay: preserve original time rows, including invalid poses and missing roles.
    public sealed class RecordedPoseSource : IPoseSource
    {
        public string Kind => "recorded_pose_replay";
        public IReadOnlyList<PoseFrame> Frames => frames;
        private readonly List<PoseFrame> frames = new List<PoseFrame>();
        private int index;
        public RecordedPoseSource(string path)
        {
            using var reader = new StreamReader(path);
            if (reader.ReadLine() != "acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason")
                throw new InvalidDataException("対応するposes.csvの列ではありません。");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var c = Csv(line);
                if (c.Count != 14) throw new InvalidDataException("poses.csvの列数が不正です。");
                double time = double.Parse(c[0], CultureInfo.InvariantCulture);
                if (!double.IsFinite(time) || frames.Count > 0 && time < frames[frames.Count - 1].time) throw new InvalidDataException("時刻列が不正です。");
                if (frames.Count == 0 || frames[frames.Count - 1].time != time) frames.Add(new PoseFrame { time = time });
                // An absent device stays absent. A fabricated ID would turn temporary loss into a reconnection.
                if (string.IsNullOrEmpty(c[4])) continue;
                bool valid = bool.Parse(c[5]);
                var p = new TrackerPose { acquiredTime = time, key = c[3], runtimeDeviceId = int.Parse(c[4]),
                    valid = valid, inputSystemTime = string.IsNullOrEmpty(c[1]) ? null : double.Parse(c[1], CultureInfo.InvariantCulture), invalidReason = c[13] };
                if (valid) { p.position = new Vector3(F(c[6]), F(c[7]), F(c[8])); p.rotation = new Quaternion(F(c[9]), F(c[10]), F(c[11]), F(c[12])); }
                frames[frames.Count - 1].poses.Add(p);
            }
        }
        private static float F(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        public PoseFrame Read(double acquiredTime) => index < frames.Count ? frames[index++] : null;
        public void Dispose() { }
        internal static List<string> Csv(string line)
        {
            var result = new List<string>(); var value = new System.Text.StringBuilder(); bool quote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (quote && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; } else quote = !quote; }
                else if (c == ',' && !quote) { result.Add(value.ToString()); value.Clear(); } else value.Append(c);
            }
            if (quote) throw new InvalidDataException("poses.csvの引用符が閉じていません。");
            result.Add(value.ToString()); return result;
        }
    }
}
