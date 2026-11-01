using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class ResearchSessionSettings
    {
        public int format = 2,
            repetition;
        public string participant_id,
            visit_id,
            trial_id,
            phase,
            task,
            group,
            input_source,
            created_utc;
        public string application_version,
            unity_version,
            source_commit;
        public string target_device = "VIVE Focus Vision",
            tracker_product = "VIVE Ultimate Tracker";
        public string vive_openxr_version = "2.5.1",
            unity_openxr_version = "1.18.0";
        public string timestamp_clock =
            "Unity software acquisition / Input System; sensor timestamps unavailable";
        public string measurement_validation = "physical_device_accuracy_and_output_pending";
        public string formal_task_time = "PC operator.json, linked by trial_id";
        public FeedbackSettings feedback = new FeedbackSettings();
        public float digital_volume;
    }

    public sealed class ResearchSessionLog : IDisposable
    {
        readonly StreamWriter poses,
            speeds,
            events,
            diagnostics;
        public EyeTrackingLog EyeLog { get; private set; }
        public string DirectoryPath { get; }
        bool closed;

        public ResearchSessionLog(
            string root,
            ResearchSessionSettings settings,
            TrackingCalibration calibration,
            string eyeSource = "unavailable",
            bool eyeCalibrated = false
        )
        {
            FileStorage.EnsureReserve(root);
            DirectoryPath = Path.Combine(root, settings.trial_id);
            if (Directory.Exists(DirectoryPath))
                throw new IOException("同じ試行IDが既に存在します。");
            Directory.CreateDirectory(DirectoryPath);
            try
            {
                File.WriteAllText(
                    Path.Combine(DirectoryPath, "session.json"),
                    JsonUtility.ToJson(settings, true)
                );
                File.WriteAllText(
                    Path.Combine(DirectoryPath, "calibration.json"),
                    JsonUtility.ToJson(calibration, true)
                );
                poses = Writer(
                    "poses.csv",
                    "acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason"
                );
                speeds = Writer(
                    "speeds.csv",
                    "software_time_s,valid,fresh,left_raw_rps,right_raw_rps,left_filtered_rps,right_filtered_rps,left_output,right_output,feedback_enabled,reason"
                );
                events = Writer("events.csv", "software_time_s,event,reason");
                diagnostics = Writer("diagnostics.csv", "software_time_s,frame_interval_s");
                EyeLog = new EyeTrackingLog(DirectoryPath, eyeSource, eyeCalibrated);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        StreamWriter Writer(string name, string header)
        {
            var w = new StreamWriter(
                new FileStream(
                    Path.Combine(DirectoryPath, name),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read
                ),
                new System.Text.UTF8Encoding(false),
                16384
            );
            w.WriteLine(header);
            w.Flush();
            return w;
        }

        public void Poses(PoseFrame frame, TrackingCalibration calibration)
        {
            foreach (TrackerRole role in Enum.GetValues(typeof(TrackerRole)))
            {
                var binding = calibration.roles[(int)role];
                var p = frame.Find(binding.key);
                bool identityMatches = p != null && p.runtimeDeviceId == binding.runtimeDeviceId;
                bool valid =
                    identityMatches
                    && p.valid
                    && TrackingGeometry.Finite(p.position)
                    && TrackingGeometry.Finite(p.rotation);
                poses.WriteLine(
                    string.Join(
                        ",",
                        new[]
                        {
                            CsvValue.Number(frame.time),
                            CsvValue.Number(p?.inputSystemTime),
                            role.ToString(),
                            CsvValue.Escape(binding.key),
                            p?.runtimeDeviceId.ToString(CultureInfo.InvariantCulture) ?? "",
                            valid ? "true" : "false",
                            valid ? CsvValue.Number(p.position.x) : "",
                            valid ? CsvValue.Number(p.position.y) : "",
                            valid ? CsvValue.Number(p.position.z) : "",
                            valid ? CsvValue.Number(p.rotation.x) : "",
                            valid ? CsvValue.Number(p.rotation.y) : "",
                            valid ? CsvValue.Number(p.rotation.z) : "",
                            valid ? CsvValue.Number(p.rotation.w) : "",
                            CsvValue.Escape(
                                p == null ? "missing_pose"
                                : !identityMatches ? "device_identity_changed"
                                : valid ? ""
                                : p.invalidReason ?? "invalid_pose"
                            ),
                        }
                    )
                );
            }
        }

        public void Speed(double time, WheelSpeedSample s, BilateralFeedback f) =>
            speeds.WriteLine(
                string.Join(
                    ",",
                    new[]
                    {
                        CsvValue.Number(time),
                        s.valid.ToString(),
                        s.fresh.ToString(),
                        s.valid ? CsvValue.Number(s.leftRps) : "",
                        s.valid ? CsvValue.Number(s.rightRps) : "",
                        f.valid ? CsvValue.Number(f.left.filteredRps) : "",
                        f.valid ? CsvValue.Number(f.right.filteredRps) : "",
                        CsvValue.Number(f.left.normalized),
                        CsvValue.Number(f.right.normalized),
                        f.enabled.ToString(),
                        CsvValue.Escape(s.reason),
                    }
                )
            );

        public void Event(double time, string name, string reason = "") =>
            events.WriteLine(
                CsvValue.Number(time) + "," + CsvValue.Escape(name) + "," + CsvValue.Escape(reason)
            );

        public void Diagnostic(double time, double interval) =>
            diagnostics.WriteLine(CsvValue.Number(time) + "," + CsvValue.Number(interval));

        public void Flush()
        {
            poses.Flush();
            speeds.Flush();
            events.Flush();
            diagnostics.Flush();
            EyeLog?.Flush();
        }

        public void Complete(double time, bool trialComplete, string reason)
        {
            Event(time, trialComplete ? "record_closed" : "record_interrupted", reason);
            Flush();
            Dispose();
            File.WriteAllText(
                Path.Combine(DirectoryPath, "completed.json"),
                "{\"format\":2,\"files_closed\":true,\"trial_complete\":"
                    + (trialComplete ? "true" : "false")
                    + ",\"software_time_s\":"
                    + CsvValue.Number(time)
                    + "}"
            );
        }

        public void Dispose()
        {
            if (closed)
                return;
            closed = true;
            poses?.Dispose();
            speeds?.Dispose();
            events?.Dispose();
            diagnostics?.Dispose();
            EyeLog?.Dispose();
        }
    }
}
