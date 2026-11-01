using System;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class EyeDatum
    {
        public bool gazeValid,
            pupilDiameterValid,
            pupilPositionValid,
            geometryValid;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float pupilDiameter,
            pupilX,
            pupilY,
            openness,
            wide,
            squeeze;
    }

    public sealed class EyeTrackingFrame
    {
        public double time;
        public int frame;
        public string reason;
        public bool gazeApiAvailable,
            pupilApiAvailable,
            geometryApiAvailable;
        public EyeDatum left = new EyeDatum(),
            right = new EyeDatum();
        public string Status =>
            left.gazeValid && right.gazeValid ? "取得中"
            : left.gazeValid || right.gazeValid ? "一部欠測"
            : "取得不可";

        public void Sanitize()
        {
            left ??= new EyeDatum();
            right ??= new EyeDatum();
            foreach (var e in new[] { left, right })
            {
                e.gazeValid &=
                    TrackingGeometry.Finite(e.position) && TrackingGeometry.Finite(e.rotation);
                e.pupilDiameterValid &= float.IsFinite(e.pupilDiameter);
                e.pupilPositionValid &= float.IsFinite(e.pupilX) && float.IsFinite(e.pupilY);
                e.geometryValid &=
                    float.IsFinite(e.openness)
                    && float.IsFinite(e.wide)
                    && float.IsFinite(e.squeeze);
            }
        }
    }

    public interface IEyeTrackingSource : IDisposable
    {
        string Kind { get; }
        EyeTrackingFrame Read(double acquiredTime, int frame);
    }

    public sealed class UnavailableEyeTrackingSource : IEyeTrackingSource
    {
        public string Kind => "unavailable";

        public EyeTrackingFrame Read(double time, int frame) =>
            new EyeTrackingFrame
            {
                time = time,
                frame = frame,
                reason = "eye_source_unavailable",
            };

        public void Dispose() { }
    }

    [Serializable]
    public sealed class EyeTrackingQuality
    {
        public int format = 1,
            frames,
            leftValidFrames,
            rightValidFrames,
            bothValidFrames,
            missingFrames;
        public double leftValidRatio,
            rightValidRatio,
            bothValidRatio;
        public string source,
            sdk = "VIVE OpenXR 2.5.1",
            api = "ViveEyeTracker public array getters",
            status = "未記録",
            lastReason;
        public string clock = "Unity realtimeSinceStartupAsDouble; acquisition, not sensor time";
        public string nativeTimestamp = "not exposed by the selected SDK array API",
            coordinates = "Unity left-handed tracking space; metres; forward +Z";
        public string pupilDiameterUnit = "millimetres (XR_HTC_eye_tracker)",
            pupilPositionUnit = "SDK normalized pupil position";
        public bool eyeCalibrationConfirmed,
            sensorFrequencyVerified = false,
            physicalAccuracyVerified = false;
        public bool gazeApiSucceeded,
            pupilApiSucceeded,
            geometryApiSucceeded;
        public double firstTime,
            lastTime,
            meanIntervalSeconds,
            maxIntervalSeconds;
        public double LeftRatio => frames == 0 ? 0 : (double)leftValidFrames / frames;

        public void Add(EyeTrackingFrame sample)
        {
            if (frames == 0)
                firstTime = sample.time;
            else
                maxIntervalSeconds = Math.Max(maxIntervalSeconds, sample.time - lastTime);
            frames++;
            lastTime = sample.time;
            meanIntervalSeconds = frames < 2 ? 0 : (lastTime - firstTime) / (frames - 1);
            if (sample.left.gazeValid)
                leftValidFrames++;
            if (sample.right.gazeValid)
                rightValidFrames++;
            if (sample.left.gazeValid && sample.right.gazeValid)
                bothValidFrames++;
            else
                missingFrames++;
            leftValidRatio = (double)leftValidFrames / frames;
            rightValidRatio = (double)rightValidFrames / frames;
            bothValidRatio = (double)bothValidFrames / frames;
            status = sample.Status;
            lastReason = sample.reason;
            gazeApiSucceeded |= sample.gazeApiAvailable;
            pupilApiSucceeded |= sample.pupilApiAvailable;
            geometryApiSucceeded |= sample.geometryApiAvailable;
        }
    }
}
