using System;
namespace WheelchairSonification.Core
{
    public sealed class SpeedFeedbackEngine
    {
        readonly FeedbackSettings settings;
        WheelFeedback left, right;
        double previousTime;
        bool seeded;
        public SpeedFeedbackEngine(FeedbackSettings settings) { settings.Validate(); this.settings = settings; }
        public void Reset() { seeded = false; left = right = default; }
        public BilateralFeedback Update(double time, double leftRps, double rightRps, bool valid, StudyGroup group, bool enable)
        {
            if (!valid || !FeedbackProtocol.Finite(time) || !FeedbackProtocol.Finite(leftRps) || !FeedbackProtocol.Finite(rightRps))
            { Reset(); return new BilateralFeedback { group = group }; }
            if (seeded && time <= previousTime) return Current(group, enable);
            if (seeded && time - previousTime > settings.maximumGapSeconds) Reset();
            double alpha = seeded ? 1 - Math.Exp(-(time - previousTime) / settings.smoothingSeconds) : 1;
            left = Filter(left, leftRps, alpha); right = Filter(right, rightRps, alpha);
            previousTime = time; seeded = true;
            return Current(group, enable);
        }
        public BilateralFeedback Current(StudyGroup group, bool enable) => new BilateralFeedback { left = left, right = right, group = group, valid = seeded, enabled = enable && seeded && group != StudyGroup.NoFeedback };
        WheelFeedback Filter(WheelFeedback old, double raw, double alpha)
        {
            double filtered = old.filteredRps + alpha * (raw - old.filteredRps), magnitude = Math.Abs(filtered);
            bool active = old.active ? magnitude >= settings.stopRps : magnitude >= settings.startRps;
            double u = active ? FeedbackProtocol.Clamp(magnitude / settings.rangeRps, 0, 1) : 0;
            return new WheelFeedback { rawRps = raw, filteredRps = filtered, active = active, normalized = u,
                frequencyHz = settings.lowHz * Math.Pow(settings.highHz / settings.lowHz, u) };
        }
    }
}
