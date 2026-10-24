using System;
namespace WheelchairSonification.Core
{
    public enum StudyGroup { Auditory, Visual, NoFeedback }
    public enum StudyPhase { Preparation, Baseline, Training, ImmediateAssessment, Rest, RetentionAssessment }
    public enum StudyTask { None, Straight, FigureEight }
    public interface IClock { double Now { get; } }
    [Serializable] public sealed class FeedbackSettings
    {
        public double rangeRps = 1.5, smoothingSeconds = .05, startRps = .05, stopRps = .03;
        public double lowHz = 220, highHz = 660, fadeSeconds = .02, maximumGapSeconds = .10;
        public void Validate()
        {
            if (!(rangeRps > startRps && startRps > stopRps && stopRps >= 0 && smoothingSeconds > 0 &&
                lowHz > 0 && highHz > lowHz && highHz < 20000 && fadeSeconds > 0 && maximumGapSeconds > 0))
                throw new ArgumentException("FB設定の範囲が不正です。");
        }
    }
    public struct WheelFeedback
    {
        public double rawRps, filteredRps, normalized, frequencyHz;
        public bool active;
    }
    public struct BilateralFeedback
    {
        public WheelFeedback left, right;
        public bool valid, enabled;
        public StudyGroup group;
    }
}
