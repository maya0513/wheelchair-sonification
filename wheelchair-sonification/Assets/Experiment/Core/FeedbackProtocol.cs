using System;
namespace WheelchairSonification.Core
{
    public static class FeedbackProtocol
    {
        public const int Format = 2;
        public const double FlushIntervalSeconds = .5;
        public static double Clamp(double x, double low, double high) => Math.Max(low, Math.Min(high, x));
        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
    // The same sample generator runs in Unity's audio callback and offline WAV export.
    public sealed class StereoToneGenerator
    {
        double leftPhase, rightPhase, leftGain, rightGain, leftFrequency = 220, rightFrequency = 220;
        public void Render(float[] buffer, int channels, int sampleRate, BilateralFeedback value, double volume, double fadeSeconds = .02)
        {
            if (channels < 1 || sampleRate <= 0 || buffer.Length % channels != 0) throw new ArgumentException("音声バッファが不正です。");
            double step = 1 / (sampleRate * fadeSeconds), alpha = 1 - Math.Exp(-1 / (sampleRate * fadeSeconds));
            bool audible = value.enabled && value.valid && value.group == StudyGroup.Auditory;
            double targetLeft = audible && value.left.active ? 1 : 0;
            double targetRight = audible && value.right.active ? 1 : 0;
            for (int i = 0; i < buffer.Length; i += channels)
            {
                leftGain = Move(leftGain, targetLeft, step); rightGain = Move(rightGain, targetRight, step);
                if (value.left.frequencyHz > 0) leftFrequency += alpha * (value.left.frequencyHz - leftFrequency);
                if (value.right.frequencyHz > 0) rightFrequency += alpha * (value.right.frequencyHz - rightFrequency);
                float l = (float)(Math.Sin(leftPhase) * leftGain * FeedbackProtocol.Clamp(volume, 0, .3)), r = (float)(Math.Sin(rightPhase) * rightGain * FeedbackProtocol.Clamp(volume, 0, .3));
                buffer[i] = channels == 1 ? (l + r) * .5f : l;
                if (channels > 1) buffer[i + 1] = r;
                for (int c = 2; c < channels; c++) buffer[i + c] = 0;
                leftPhase = (leftPhase + 2 * Math.PI * leftFrequency / sampleRate) % (2 * Math.PI);
                rightPhase = (rightPhase + 2 * Math.PI * rightFrequency / sampleRate) % (2 * Math.PI);
            }
        }
        static double Move(double current, double target, double step) => current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
    }
}
