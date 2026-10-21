using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class ResearchSessionSettings
    {
        public string participant_id, visit_id, trial_id, phase, task, group, major_side, input_source;
        public int repetition;
        public string replaces_trial_id;
        public string created_utc, clock_origin_utc, session_id;
        public string unity_version, application_version, build_guid, source_commit, operating_system;
        public string target_device = "VIVE Focus Vision", tracker_product = "VIVE Ultimate Tracker";
        public string vive_openxr_version = "2.5.1", unity_openxr_version = "1.18.0";
        public string device_diagnostics = "battery_and_firmware_not_collected";
        public string pose_coordinate = "Unity OpenXR world, metres, quaternion xyzw";
        public string input_timestamp_clock = "Unity Input System (software input, not sensor time)";
        public bool sensor_timestamp_available = false, physical_output_measured = false;
        public string measurement_validation = "pending_external_video_and_device_validation";
        public bool target_available;
        public double target_degrees, baseline_median_degrees, increment_degrees;
        public double[] baseline_repeat_1, baseline_repeat_2;
        public float digital_volume;
        public double presentation_seconds = FeedbackProtocol.DurationSeconds;
        public int short_hz = 440, target_hz = 660, long_hz = 880;
    }

    // Each directory is one trial. A visit_id links the trials of one visit without overwriting them.
    public sealed class ResearchSessionLog : IExperimentLog
    {
        private readonly FileSessionLog events;
        private readonly StreamWriter poses, trials, diagnostics;
        private readonly ResearchSessionSettings settings;
        private bool disposed;
        public string DirectoryPath { get; }

        public ResearchSessionLog(string root, ResearchSessionSettings settings, TrackingCalibration calibration, double createdTime = 0)
        {
            this.settings = settings;
            var eventSettings = new DemoSessionSettings { input_source = settings.input_source };
            events = new FileSessionLog(root, eventSettings);
            DirectoryPath = events.DirectoryPath;
            try
            {
                settings.session_id = eventSettings.session_id;
                settings.created_utc = eventSettings.created_utc;
                File.WriteAllText(Path.Combine(DirectoryPath, "session.json"), JsonUtility.ToJson(settings, true));
                File.WriteAllText(Path.Combine(DirectoryPath, "calibration.json"), JsonUtility.ToJson(calibration, true));
                poses = Writer("poses.csv", "acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason");
                trials = Writer("trials.csv", "software_time_s,participant_id,visit_id,trial_id,phase,task,repetition,event,reason");
                diagnostics = Writer("diagnostics.csv", "software_time_s,event,value,reason");
                Trial(createdTime, "created", "elapsed_time_is_not_video_scored_completion_time");
            }
            catch { Dispose(); throw; }
        }

        private StreamWriter Writer(string name, string header)
        {
            var writer = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new System.Text.UTF8Encoding(false), 16384);
            writer.WriteLine(header); writer.Flush(); return writer;
        }

        public void Poses(PoseFrame frame, TrackingCalibration calibration)
        {
            foreach (TrackerRole role in Enum.GetValues(typeof(TrackerRole)))
            {
                var binding = calibration.roles[(int)role];
                var p = calibration.Pose(frame, role);
                bool valid = p != null && p.valid && TrackingGeometry.Finite(p.position) && TrackingGeometry.Finite(p.rotation);
                poses.WriteLine(string.Join(",", new[] {
                    N(frame.time), N(p?.inputSystemTime), role.ToString(), E(binding.key),
                    p?.runtimeDeviceId.ToString(CultureInfo.InvariantCulture) ?? "", valid ? "true" : "false",
                    valid ? N(p.position.x) : "", valid ? N(p.position.y) : "", valid ? N(p.position.z) : "",
                    valid ? N(p.rotation.x) : "", valid ? N(p.rotation.y) : "", valid ? N(p.rotation.z) : "", valid ? N(p.rotation.w) : "",
                    E(p == null ? "missing_or_runtime_identity_changed" : valid ? "" : p.invalidReason ?? "invalid_pose") }));
            }
        }

        public void Trial(double time, string action, string reason = "") => trials.WriteLine(string.Join(",", new[] {
            N(time), E(settings.participant_id), E(settings.visit_id), E(settings.trial_id), E(settings.phase), E(settings.task),
            settings.repetition.ToString(CultureInfo.InvariantCulture), E(action), E(reason) }));
        public void Diagnostic(double time, string action, double? value = null, string reason = "") =>
            diagnostics.WriteLine(string.Join(",", N(time), E(action), N(value), E(reason)));
        public void Write(ExperimentRecord record) => events.Write(record);
        public void Flush() { events.Flush(); poses.Flush(); trials.Flush(); diagnostics.Flush(); }
        public void Complete(double timeSeconds)
        {
            Trial(timeSeconds, "completed"); Flush();
            poses.Dispose(); trials.Dispose(); diagnostics.Dispose();
            events.Complete(timeSeconds); disposed = true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            poses?.Dispose(); trials?.Dispose(); diagnostics?.Dispose(); events?.Dispose();
        }
        internal static string N(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        internal static string E(string value) => string.IsNullOrEmpty(value) ? "" :
            value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
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
                bool valid = bool.Parse(c[5]);
                var p = new TrackerPose { acquiredTime = time, key = c[3], runtimeDeviceId = string.IsNullOrEmpty(c[4]) ? -1 : int.Parse(c[4]),
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
