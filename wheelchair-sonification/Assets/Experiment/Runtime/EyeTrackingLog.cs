using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public sealed class EyeTrackingLog : IDisposable
    {
        readonly StreamWriter eyes,
            display;
        readonly string directory;
        public EyeTrackingQuality Quality { get; }

        static string N(double? value) => CsvValue.Number(value);

        static string B(bool value) => value ? "true" : "false";

        public EyeTrackingLog(string directory, string source, bool calibrated)
        {
            this.directory = directory;
            Quality = new EyeTrackingQuality
            {
                source = source,
                eyeCalibrationConfirmed = calibrated,
            };
            try
            {
                eyes = Writer(
                    "eyes.csv",
                    "software_time_s,frame,native_time,eye,gaze_valid,gaze_x_m,gaze_y_m,gaze_z_m,gaze_qx,gaze_qy,gaze_qz,gaze_qw,direction_x,direction_y,direction_z,pupil_diameter_valid,pupil_diameter_mm,pupil_position_valid,pupil_x,pupil_y,geometry_valid,openness,wide,squeeze,head_valid,head_x_m,head_y_m,head_z_m,head_qx,head_qy,head_qz,head_qw,reason"
                );
                display = Writer(
                    "display.csv",
                    "software_time_s,frame,anchor_valid,visible,left_output,right_output,display_x_m,display_y_m,display_z_m,display_qx,display_qy,display_qz,display_qw,origin_x_m,origin_y_m,origin_z_m,origin_qx,origin_qy,origin_qz,origin_qw"
                );
                SaveSummary();
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
                    Path.Combine(directory, name),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read
                )
            );
            w.WriteLine(header);
            w.Flush();
            return w;
        }

        static void Pose(List<string> row, bool valid, Vector3 p, Quaternion q)
        {
            row.AddRange(
                valid
                    ? new[] { N(p.x), N(p.y), N(p.z), N(q.x), N(q.y), N(q.z), N(q.w) }
                    : new[] { "", "", "", "", "", "", "" }
            );
        }

        public void Write(EyeTrackingFrame f, DisplayObservation d)
        {
            Quality.Add(f);
            for (int i = 0; i < 2; i++)
            {
                var e = i == 0 ? f.left : f.right;
                var row = new List<string>
                {
                    N(f.time),
                    f.frame.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "",
                    i == 0 ? "left" : "right",
                    B(e.gazeValid),
                };
                Pose(row, e.gazeValid, e.position, e.rotation);
                var direction = e.rotation * Vector3.forward;
                row.AddRange(
                    e.gazeValid
                        ? new[] { N(direction.x), N(direction.y), N(direction.z) }
                        : new[] { "", "", "" }
                );
                row.AddRange(
                    new[]
                    {
                        B(e.pupilDiameterValid),
                        e.pupilDiameterValid ? N(e.pupilDiameter) : "",
                        B(e.pupilPositionValid),
                        e.pupilPositionValid ? N(e.pupilX) : "",
                        e.pupilPositionValid ? N(e.pupilY) : "",
                        B(e.geometryValid),
                        e.geometryValid ? N(e.openness) : "",
                        e.geometryValid ? N(e.wide) : "",
                        e.geometryValid ? N(e.squeeze) : "",
                        B(d.headValid),
                    }
                );
                Pose(row, d.headValid, d.headPosition, d.headRotation);
                row.Add(CsvValue.Escape(f.reason));
                eyes.WriteLine(string.Join(",", row));
            }
            var dr = new List<string>
            {
                N(f.time),
                f.frame.ToString(System.Globalization.CultureInfo.InvariantCulture),
                B(d.anchorValid),
                B(d.visible),
                N(d.left),
                N(d.right),
            };
            Pose(dr, d.anchorValid, d.displayPosition, d.displayRotation);
            Pose(dr, true, d.originPosition, d.originRotation);
            display.WriteLine(string.Join(",", dr));
            if (Quality.frames == 1)
                File.WriteAllText(
                    Path.Combine(directory, "display.json"),
                    JsonUtility.ToJson(d.placement, true)
                );
        }

        void SaveSummary() =>
            File.WriteAllText(
                Path.Combine(directory, "eye-tracking.json"),
                JsonUtility.ToJson(Quality, true)
            );

        public void Flush()
        {
            eyes.Flush();
            display.Flush();
            SaveSummary();
        }

        public void Dispose()
        {
            eyes?.Dispose();
            display?.Dispose();
        }
    }
}
