using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
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
            if (
                reader.ReadLine()
                != "acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason"
            )
                throw new InvalidDataException("対応するposes.csvの列ではありません。");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var c = Csv(line);
                if (c.Count != 14)
                    throw new InvalidDataException("poses.csvの列数が不正です。");
                double time = double.Parse(c[0], CultureInfo.InvariantCulture);
                if (
                    !double.IsFinite(time)
                    || frames.Count > 0 && time < frames[frames.Count - 1].time
                )
                    throw new InvalidDataException("時刻列が不正です。");
                if (frames.Count == 0 || frames[frames.Count - 1].time != time)
                    frames.Add(new PoseFrame { time = time });
                // An absent device stays absent. A fabricated ID would turn temporary loss into a reconnection.
                if (string.IsNullOrEmpty(c[4]))
                    continue;
                bool valid = bool.Parse(c[5]);
                var p = new TrackerPose
                {
                    acquiredTime = time,
                    key = c[3],
                    runtimeDeviceId = int.Parse(c[4]),
                    valid = valid,
                    inputSystemTime = string.IsNullOrEmpty(c[1])
                        ? null
                        : double.Parse(c[1], CultureInfo.InvariantCulture),
                    invalidReason = c[13],
                };
                if (valid)
                {
                    p.position = new Vector3(F(c[6]), F(c[7]), F(c[8]));
                    p.rotation = new Quaternion(F(c[9]), F(c[10]), F(c[11]), F(c[12]));
                }
                frames[frames.Count - 1].poses.Add(p);
            }
        }

        private static float F(string value) => float.Parse(value, CultureInfo.InvariantCulture);

        public PoseFrame Read(double acquiredTime) => index < frames.Count ? frames[index++] : null;

        public void Dispose() { }

        internal static List<string> Csv(string line)
        {
            var result = new List<string>();
            var value = new System.Text.StringBuilder();
            bool quote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quote && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                        quote = !quote;
                }
                else if (c == ',' && !quote)
                {
                    result.Add(value.ToString());
                    value.Clear();
                }
                else
                    value.Append(c);
            }
            if (quote)
                throw new InvalidDataException("poses.csvの引用符が閉じていません。");
            result.Add(value.ToString());
            return result;
        }
    }
}
