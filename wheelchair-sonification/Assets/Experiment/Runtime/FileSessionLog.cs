using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class DemoSessionSettings
    {
        public string input_source = "simulated_angle_events";
        public bool physical_output_measured = false;
        public bool sensor_timestamp_available = false;
        public string angle_unit = "degrees";
        public string time_unit = "seconds";
        public string clock = "Stopwatch";
        public string clock_origin_utc;
        public string created_utc;
        public string session_id;
        public string group;
        public double[] baseline_repeat_1;
        public double[] baseline_repeat_2;
        public double baseline_median_degrees;
        public double target_degrees;
        public double increment_degrees;
        public float digital_volume;
        public double maximum_increase_degrees = FeedbackProtocol.MaximumIncreaseDegrees;
        public double increase_cap_degrees = FeedbackProtocol.IncreaseCapDegrees;
        public double tolerance_degrees = FeedbackProtocol.ToleranceDegrees;
        public double presentation_seconds = FeedbackProtocol.DurationSeconds;
        public double fade_seconds = FeedbackProtocol.FadeSeconds;
        public double flush_interval_seconds = FeedbackProtocol.FlushIntervalSeconds;
        public int short_hz = FeedbackProtocol.Frequency(FeedbackState.Short);
        public int target_hz = FeedbackProtocol.Frequency(FeedbackState.Target);
        public int long_hz = FeedbackProtocol.Frequency(FeedbackState.Long);
        public int sample_rate_hz = FeedbackProtocol.SampleRate;
        public int channels = FeedbackProtocol.Channels;
        public string unity_version;
        public string build_guid;
        public string operating_system;
    }

    // Immutable settings + append-only events + an atomic completion marker.
    // A session without a valid completed.json remains incomplete after restart.
    public sealed class FileSessionLog : IExperimentLog
    {
        public const string Header = "sequence,software_time_s,input_time_s,event,stroke_id,angle_degrees,state,reason,group,phase,task,motion,in_central_area,tracking_valid,audio_dsp_time_s";
        private readonly StreamWriter writer;
        private readonly string sessionId;
        private bool disposed;
        public string DirectoryPath { get; }

        [Serializable]
        private sealed class Completion
        {
            public bool completed;
            public string session_id;
            public string completed_utc;
            public double software_time_s;
        }

        public FileSessionLog(string root, DemoSessionSettings settings, string requestedId = null)
        {
            sessionId = requestedId ?? DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(sessionId) || Path.GetFileName(sessionId) != sessionId ||
                sessionId == "." || sessionId == "..") throw new ArgumentException("Invalid session ID.");
            DirectoryPath = Path.Combine(root, sessionId);
            Directory.CreateDirectory(DirectoryPath);
            settings.session_id = sessionId;
            settings.created_utc = DateTime.UtcNow.ToString("O");
            WriteNew(Path.Combine(DirectoryPath, "session.json"), JsonUtility.ToJson(settings, true));
            try
            {
                writer = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, "events.csv"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                writer.WriteLine(Header);
                writer.Flush();
            }
            catch
            {
                writer?.Dispose();
                throw;
            }
        }

        public void Write(ExperimentRecord record)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FileSessionLog));
            var context = record.Context;
            writer.WriteLine(string.Join(",", new[]
            {
                record.Sequence.ToString(CultureInfo.InvariantCulture), Number(record.TimeSeconds),
                Number(record.InputTimeSeconds), Escape(record.Event), record.StrokeId.ToString(CultureInfo.InvariantCulture),
                Number(record.AngleDegrees), record.Feedback?.ToString().ToUpperInvariant() ?? "",
                Escape(record.Reason), record.Group.ToString(), context.Phase.ToString(), context.Task.ToString(),
                context.Motion.ToString(), context.InCentralArea ? "true" : "false", context.TrackingValid ? "true" : "false",
                Number(record.AudioDspTimeSeconds)
            }));
        }

        public void Flush()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FileSessionLog));
            writer.Flush();
        }

        public void Complete(double timeSeconds)
        {
            Flush();
            writer.Dispose();
            disposed = true;
            string temporary = Path.Combine(DirectoryPath, "completed.tmp");
            WriteNew(temporary, JsonUtility.ToJson(new Completion
            {
                completed = true, session_id = sessionId,
                completed_utc = DateTime.UtcNow.ToString("O"), software_time_s = timeSeconds
            }, true));
            File.Move(temporary, Path.Combine(DirectoryPath, "completed.json"));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            writer.Dispose();
        }

        public static int CountIncomplete(string root)
        {
            if (!Directory.Exists(root)) return 0;
            int count = 0;
            foreach (string directory in Directory.GetDirectories(root))
            {
                if (!File.Exists(Path.Combine(directory, "session.json"))) continue;
                string completionPath = Path.Combine(directory, "completed.json");
                bool complete = false;
                if (File.Exists(completionPath))
                {
                    try
                    {
                        var completion = JsonUtility.FromJson<Completion>(File.ReadAllText(completionPath));
                        complete = completion != null && completion.completed &&
                            completion.session_id == Path.GetFileName(directory);
                    }
                    catch (ArgumentException) { }
                }
                if (!complete) count++;
            }
            return count;
        }

        private static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? value :
                "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        private static void WriteNew(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var textWriter = new StreamWriter(stream, new UTF8Encoding(false)))
                textWriter.Write(text);
        }
    }
}
