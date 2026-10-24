using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    public static class PoseReplay
    {
        [Serializable] public sealed class Report { public int frames; public bool classifications_match, physical_output_measured = false; public string format = "wheel-speed-2"; }
        public static Report Run(string directory, string output)
        {
            var settings = JsonUtility.FromJson<ResearchSessionSettings>(File.ReadAllText(Path.Combine(directory,"session.json")));
            var calibration = JsonUtility.FromJson<TrackingCalibration>(File.ReadAllText(Path.Combine(directory,"calibration.json")));
            if(settings.format!=2||calibration.format!=2||!calibration.Complete)throw new InvalidDataException("形式2の車輪速度記録と有効な開始時校正が必要です。旧接触角記録は読みません。");
            var originals=File.ReadLines(Path.Combine(directory,"speeds.csv")).Skip(1).Select(RecordedPoseSource.Csv).ToArray();
            var ends=File.ReadLines(Path.Combine(directory,"events.csv")).Skip(1).Select(RecordedPoseSource.Csv).Where(r=>r[1]=="training_feedback_ended").ToArray();
            double end=ends.Length>0?double.Parse(ends[0][0],CultureInfo.InvariantCulture):double.PositiveInfinity;
            var group=(StudyGroup)Enum.Parse(typeof(StudyGroup),settings.group);
            bool training=settings.phase=="Training";
            var estimator=new WheelSpeedEstimator(calibration,settings.feedback.maximumGapSeconds);var engine=new SpeedFeedbackEngine(settings.feedback);
            using var poses=new RecordedPoseSource(Path.Combine(directory,"poses.csv"));
            bool match=poses.Frames.Count==originals.Length;int count=0;
            Directory.CreateDirectory(output);using var writer=new StreamWriter(Path.Combine(output,"replayed-speeds.csv"));writer.WriteLine("software_time_s,left_raw_rps,right_raw_rps,left_output,right_output,valid,feedback_enabled");
            foreach(var frame in poses.Frames)
            {
                var s=estimator.Update(frame);var f=engine.Update(frame.time,s.leftRps,s.rightRps,s.valid,group,training&&frame.time<end);
                writer.WriteLine(string.Join(",",ResearchSessionLog.N(frame.time),ResearchSessionLog.N(s.leftRps),ResearchSessionLog.N(s.rightRps),ResearchSessionLog.N(f.left.normalized),ResearchSessionLog.N(f.right.normalized),s.valid,f.enabled));
                if(count<originals.Length)
                {
                    var r=originals[count];match&=r[1].Equals(s.valid.ToString(),StringComparison.OrdinalIgnoreCase)&&r[9].Equals(f.enabled.ToString(),StringComparison.OrdinalIgnoreCase);
                    if(s.valid)match&=Near(r[3],s.leftRps)&&Near(r[4],s.rightRps)&&Near(r[5],f.left.filteredRps)&&Near(r[6],f.right.filteredRps);
                    match&=Near(r[7],f.left.normalized)&&Near(r[8],f.right.normalized);
                }
                count++;
            }
            var report=new Report{frames=count,classifications_match=match};File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));return report;
        }
        static bool Near(string s,double value)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&Math.Abs(n-value)<.0001;
    }
}
