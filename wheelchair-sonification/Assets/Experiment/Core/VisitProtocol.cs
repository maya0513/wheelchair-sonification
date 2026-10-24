using System;
namespace WheelchairSonification.Core
{
    // Selection is manual. No visit sequencing, quiz gates, or minimum-rest lockout.
    [Serializable] public sealed class TrialSelection
    {
        public StudyPhase phase = StudyPhase.Baseline;
        public StudyTask task = StudyTask.FigureEight;
        public int repetition = 1;
        public bool FeedbackAllowed => phase == StudyPhase.Training && (task == StudyTask.Straight || task == StudyTask.FigureEight);
    }
}
