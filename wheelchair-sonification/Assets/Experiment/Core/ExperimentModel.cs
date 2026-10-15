using System;

namespace WheelchairSonification.Core
{
    public enum StudyGroup { Auditory, Visual, NoFeedback }
    public enum StudyPhase { Preparation, Baseline, Training, ImmediateAssessment, Rest, RetentionAssessment }
    public enum StudyTask { None, Straight, FigureEight }
    public enum MotionState { Straight, Stopped, Braking, Turning }
    public enum FeedbackState { Short, Target, Long }
    public enum SessionState { Running, Completed, Faulted }
    public enum InputKind { ContextChanged, ContactStarted, Released }

    public readonly struct ExperimentContext
    {
        public readonly StudyPhase Phase;
        public readonly StudyTask Task;
        public readonly MotionState Motion;
        public readonly bool InCentralArea;
        public readonly bool TrackingValid;

        public ExperimentContext(StudyPhase phase, StudyTask task, MotionState motion,
            bool inCentralArea, bool trackingValid)
        {
            Phase = phase;
            Task = task;
            Motion = motion;
            InCentralArea = inCentralArea;
            TrackingValid = trackingValid;
        }

        public static ExperimentContext Preparation => new ExperimentContext(
            StudyPhase.Preparation, StudyTask.None, MotionState.Stopped, false, true);

        public string MeasurementBlockReason
        {
            get
            {
                if (!TrackingValid) return "tracking_invalid";
                if (Task != StudyTask.Straight) return "not_straight_task";
                if (Motion != MotionState.Straight) return "motion_" + Motion.ToString().ToLowerInvariant();
                if (!InCentralArea) return "outside_central_area";
                if (Phase == StudyPhase.Preparation || Phase == StudyPhase.Rest) return "not_measurement_phase";
                return null;
            }
        }

        public string FeedbackBlockReason => MeasurementBlockReason ??
            (Phase == StudyPhase.Training ? null : "not_training");
    }

    public readonly struct ExperimentInput
    {
        public readonly InputKind Kind;
        public readonly double TimestampSeconds;
        public readonly double AngleDegrees;
        public readonly ExperimentContext Context;

        private ExperimentInput(InputKind kind, double time, double angle, ExperimentContext context)
        {
            Kind = kind;
            TimestampSeconds = time;
            AngleDegrees = angle;
            Context = context;
        }

        public static ExperimentInput Contact(double time) =>
            new ExperimentInput(InputKind.ContactStarted, time, double.NaN, default);
        public static ExperimentInput Release(double time, double angle) =>
            new ExperimentInput(InputKind.Released, time, angle, default);
        public static ExperimentInput ChangeContext(double time, ExperimentContext context) =>
            new ExperimentInput(InputKind.ContextChanged, time, double.NaN, context);
    }

    public interface IExperimentInput
    {
        event Action<ExperimentInput> Received;
    }

    public interface IClock { double Now { get; } }

    public interface IFeedbackOutput
    {
        // This receipt is a software/DSP timestamp, never a measurement of physical output.
        double? Begin(StudyGroup group, FeedbackState state);
        void Stop();
        void SetGaugeVisible(bool visible);
    }

    public interface IExperimentLog : IDisposable
    {
        void Write(ExperimentRecord record);
        void Flush();
        void Complete(double timeSeconds);
    }

    public sealed class ExperimentRecord
    {
        public long Sequence;
        public double TimeSeconds;
        public double? InputTimeSeconds;
        public string Event;
        public long StrokeId;
        public double? AngleDegrees;
        public FeedbackState? Feedback;
        public string Reason;
        public StudyGroup Group;
        public ExperimentContext Context;
        public double? AudioDspTimeSeconds;
    }
}
