using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public sealed class FrameDispatchClock : IClock { public double Now { get; set; } }

    public static class PoseReplay
    {
        [Serializable]
        public sealed class Report
        {
            public string session_id, input_source = "recorded_pose_replay";
            public bool physical_output_measured = false, original_completed;
            public int frames, original_classifications, replayed_classifications;
            public bool classifications_match;
        }
        private sealed class Sink : IExperimentLog, IFeedbackOutput
        {
            public readonly List<ExperimentRecord> records = new List<ExperimentRecord>();
            public void Write(ExperimentRecord value) => records.Add(value);
            public void Flush() { }
            public void Complete(double time) { }
            public void Dispose() { }
            public double? Begin(StudyGroup group, FeedbackState state) => null;
            public void Stop() { }
            public void SetGaugeVisible(bool value) { }
        }

        public static string Latest(string root, string outputRoot)
        {
            if (!Directory.Exists(root)) throw new InvalidOperationException("実測記録がありません。");
            string directory = Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "poses.csv")))
                .OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
            if (directory == null) throw new InvalidOperationException("再解析できる実測記録がありません。");
            var report = Run(directory, Path.Combine(outputRoot, Path.GetFileName(directory) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            return report.classifications_match ? "再解析完了。元記録の判定と一致。" : "再解析完了。元記録との不一致を確認してください。";
        }

        public static Report Run(string directory, string output)
        {
            var settings = JsonUtility.FromJson<ResearchSessionSettings>(File.ReadAllText(Path.Combine(directory, "session.json")));
            var calibration = JsonUtility.FromJson<TrackingCalibration>(File.ReadAllText(Path.Combine(directory, "calibration.json")));
            if (!calibration.Complete) throw new InvalidDataException("完了した校正データがありません。");
            var originals = File.ReadLines(Path.Combine(directory, "events.csv")).Skip(1).Select(RecordedPoseSource.Csv).ToList();
            var start = originals.Find(row => row[3] == "session_started");
            if (start == null) throw new InvalidDataException("準備中に中断されたため、推進判定の再解析対象がありません。");
            double startTime = double.Parse(start[1], CultureInfo.InvariantCulture);
            var clock = new FrameDispatchClock { Now = startTime };
            var sink = new Sink();
            var estimator = new PushEstimator(calibration, settings.major_side == "right");
            var group = settings.group == "unallocated" ? StudyGroup.NoFeedback : (StudyGroup)Enum.Parse(typeof(StudyGroup), settings.group);
            using var engine = new ExperimentEngine(clock, sink, sink, group, settings.target_available ? settings.target_degrees : (double?)null);
            estimator.Received += engine.Process;
            using var poses = new RecordedPoseSource(Path.Combine(directory, "poses.csv"));
            double windowEndFrame = double.PositiveInfinity;
            string controls = Path.Combine(directory, "trials.csv");
            if (File.Exists(controls))
            {
                var ending = File.ReadLines(controls).Skip(1).Select(RecordedPoseSource.Csv).FirstOrDefault(row => row.Count == 9 && row[7] == "training_window_ended");
                if (ending != null)
                {
                    double actionTime = double.Parse(ending[0], CultureInfo.InvariantCulture);
                    windowEndFrame = poses.Frames.LastOrDefault(f => f.time <= actionTime)?.time ?? double.PositiveInfinity;
                }
            }
            var phase = (StudyPhase)Enum.Parse(typeof(StudyPhase), settings.phase);
            var task = (StudyTask)Enum.Parse(typeof(StudyTask), settings.task);
            int count = 0;
            foreach (var frame in poses.Frames)
            {
                if (frame.time <= startTime) continue;
                clock.Now = frame.time;
                estimator.Observe(frame, frame.time > windowEndFrame ? StudyPhase.Preparation : phase, task);
                engine.Tick();
                if (frame.time == windowEndFrame) engine.Process(ExperimentInput.ChangeContext(frame.time, ExperimentContext.Preparation));
                count++;
            }
            if (count == 0) throw new InvalidDataException("開始後の位置姿勢がなく、推進判定を再解析できません。");
            if (originals.Any(row => row[3] == "session_completed")) engine.EndSession();
            var expected = originals.Where(row => IsClassification(row[3])).ToList();
            var actual = sink.records.Where(r => IsClassification(r.Event)).ToList();
            bool match = expected.Count == actual.Count;
            for (int i = 0; match && i < expected.Count; i++)
            {
                var row = expected[i]; var record = actual[i];
                double angle = string.IsNullOrEmpty(row[5]) ? double.NaN : double.Parse(row[5], CultureInfo.InvariantCulture);
                double other = record.AngleDegrees ?? double.NaN;
                bool angleMatch = double.IsNaN(angle) && double.IsNaN(other) || Math.Abs(angle - other) <= .0001;
                match &= row[3] == record.Event && row[6] == (record.Feedback?.ToString().ToUpperInvariant() ?? "") &&
                    row[7] == (record.Reason ?? "") && angleMatch;
            }
            Directory.CreateDirectory(output);
            using (var writer = new StreamWriter(Path.Combine(output, "replayed-classifications.csv")))
            {
                writer.WriteLine("software_time_s,event,angle_degrees,state,reason");
                foreach (var record in actual) writer.WriteLine(string.Join(",", ResearchSessionLog.N(record.TimeSeconds), record.Event,
                    ResearchSessionLog.N(record.AngleDegrees), record.Feedback?.ToString().ToUpperInvariant() ?? "", ResearchSessionLog.E(record.Reason)));
            }
            var report = new Report { session_id = settings.session_id, frames = count, original_classifications = expected.Count,
                replayed_classifications = actual.Count, classifications_match = match, original_completed = File.Exists(Path.Combine(directory, "completed.json")) };
            File.WriteAllText(Path.Combine(output, "replay-result.json"), JsonUtility.ToJson(report, true));
            return report;
        }
        private static bool IsClassification(string name) => name == "stroke_measured" || name == "stroke_classified" || name == "stroke_invalid";
    }
}
