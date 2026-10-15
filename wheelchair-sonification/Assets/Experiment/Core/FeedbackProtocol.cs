using System;
using System.Collections.Generic;

namespace WheelchairSonification.Core
{
    public static class FeedbackProtocol
    {
        public const double MaximumIncreaseDegrees = 10;
        public const double IncreaseCapDegrees = 95;
        public const double ToleranceDegrees = 5;
        public const double DurationSeconds = 0.300;
        public const double FadeSeconds = 0.010;
        public const double FlushIntervalSeconds = 0.5;
        public const int SampleRate = 48000;
        public const int Channels = 2;

        public static int Frequency(FeedbackState state)
        {
            switch (state)
            {
                case FeedbackState.Short: return 440;
                case FeedbackState.Target: return 660;
                case FeedbackState.Long: return 880;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static FeedbackState Classify(double angle, double target)
        {
            if (!IsFinite(angle) || angle <= 0 || !IsFinite(target) || target <= 0)
                throw new ArgumentException("Angles must be finite and positive.");
            if (angle < target - ToleranceDegrees) return FeedbackState.Short;
            if (angle > target + ToleranceDegrees) return FeedbackState.Long;
            return FeedbackState.Target;
        }

        public static TargetResult CalculateTarget(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            if (first == null || second == null || first.Count < 1 || second.Count < 1 ||
                first.Count + second.Count < 4)
                return TargetResult.Failure("各反復1本以上、合計4本以上の有効角が必要です。");
            var angles = new List<double>(first.Count + second.Count);
            foreach (var value in first) angles.Add(value);
            foreach (var value in second) angles.Add(value);
            foreach (var value in angles)
                if (!IsFinite(value) || value <= 0)
                    return TargetResult.Failure("基準角には正の有限値だけを入力してください。");
            angles.Sort();
            int middle = angles.Count / 2;
            double baseline = angles.Count % 2 == 1 ? angles[middle] :
                angles[middle - 1] / 2 + angles[middle] / 2;
            double increment = Math.Min(MaximumIncreaseDegrees, Math.Max(0, IncreaseCapDegrees - baseline));
            return new TargetResult(true, baseline, baseline + increment, increment, null);
        }
    }

    public readonly struct TargetResult
    {
        public readonly bool Success;
        public readonly double BaselineDegrees;
        public readonly double TargetDegrees;
        public readonly double IncrementDegrees;
        public readonly string Error;
        public TargetResult(bool success, double baseline, double target, double increment, string error)
        {
            Success = success;
            BaselineDegrees = baseline;
            TargetDegrees = target;
            IncrementDegrees = increment;
            Error = error;
        }
        public static TargetResult Failure(string error) => new TargetResult(false, 0, 0, 0, error);
    }

    public static class ToneGenerator
    {
        public static float[] CreateStereo(FeedbackState state)
        {
            int frames = (int)Math.Round(FeedbackProtocol.SampleRate * FeedbackProtocol.DurationSeconds);
            int fadeFrames = (int)Math.Round(FeedbackProtocol.SampleRate * FeedbackProtocol.FadeSeconds);
            var data = new float[frames * FeedbackProtocol.Channels];
            double frequency = FeedbackProtocol.Frequency(state);
            for (int i = 0; i < frames; i++)
            {
                double envelope = Math.Min(1, Math.Min((double)i / fadeFrames,
                    (double)(frames - 1 - i) / fadeFrames));
                float sample = (float)(Math.Sin(2 * Math.PI * frequency * i / FeedbackProtocol.SampleRate) * envelope);
                data[i * 2] = sample;
                data[i * 2 + 1] = sample;
            }
            return data;
        }
    }
}
