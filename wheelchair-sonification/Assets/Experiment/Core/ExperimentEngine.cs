using System;

namespace WheelchairSonification.Core
{
    // One contact, at most one pending presentation and one running presentation.
    // Unity drives Tick, but all decisions use the injected monotonic clock.
    public sealed class ExperimentEngine : IDisposable
    {
        private readonly IClock clock;
        private readonly IFeedbackOutput output;
        private readonly IExperimentLog log;
        private readonly StudyGroup group;
        private readonly double target;
        private long sequence;
        private long strokeId;
        private bool contactOpen;
        private string invalidStrokeReason;
        private Candidate pending;
        private Candidate active;
        private double activeStarted;
        private double lastInputTime = double.NegativeInfinity;
        private double lastFlush;
        private bool disposed;

        private sealed class Candidate
        {
            public long Stroke;
            public double Angle;
            public FeedbackState State;
        }

        public SessionState State { get; private set; } = SessionState.Running;
        public ExperimentContext Context { get; private set; } = ExperimentContext.Preparation;
        public string Fault { get; private set; }
        public bool HasOpenContact => contactOpen;
        public bool HasPendingFeedback => pending != null;
        public bool HasActiveFeedback => active != null;
        public bool GaugeVisible => State == SessionState.Running && group == StudyGroup.Visual &&
            Context.Phase == StudyPhase.Training && Context.Task == StudyTask.Straight;
        public event Action<ExperimentRecord> Recorded;

        public ExperimentEngine(IClock clock, IFeedbackOutput output, IExperimentLog log,
            StudyGroup group, double targetDegrees)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.output = output ?? throw new ArgumentNullException(nameof(output));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            if (!FeedbackProtocol.IsFinite(targetDegrees) || targetDegrees <= 0)
                throw new ArgumentException("A valid, fixed target is required.", nameof(targetDegrees));
            this.group = group;
            target = targetDegrees;
            lastFlush = clock.Now;
            Guard(() =>
            {
                output.Stop();
                output.SetGaugeVisible(false);
                Record("session_started");
                log.Flush();
            });
        }

        public void Process(ExperimentInput input)
        {
            Guard(() =>
            {
                if (!FeedbackProtocol.IsFinite(input.TimestampSeconds) || input.TimestampSeconds < lastInputTime ||
                    input.TimestampSeconds > clock.Now)
                {
                    Invalidate("invalid_input_time");
                    CancelAll("invalid_input_time");
                    Record("input_rejected", reason: "invalid_input_time", sourceTime: input.TimestampSeconds);
                    return;
                }
                lastInputTime = input.TimestampSeconds;
                switch (input.Kind)
                {
                    case InputKind.ContextChanged: ChangeContext(input); break;
                    case InputKind.ContactStarted: StartContact(input); break;
                    case InputKind.Released: Release(input); break;
                    default: throw new ArgumentOutOfRangeException(nameof(input.Kind));
                }
            });
        }

        public void Tick()
        {
            Guard(() =>
            {
                if (active != null && clock.Now >= activeStarted + FeedbackProtocol.DurationSeconds)
                    EndActive("duration_elapsed");
                if (pending != null)
                {
                    string reason = Context.FeedbackBlockReason;
                    if (contactOpen) reason = "next_contact_before_start";
                    if (reason != null) CancelPending(reason);
                    else
                    {
                        var candidate = pending;
                        pending = null;
                        double? dspTime = output.Begin(group, candidate.State);
                        active = candidate;
                        activeStarted = clock.Now;
                        Record("feedback_software_started", candidate, dspTime: dspTime);
                    }
                }
                if (clock.Now - lastFlush >= FeedbackProtocol.FlushIntervalSeconds)
                {
                    log.Flush();
                    lastFlush = clock.Now;
                }
            });
        }

        private void ChangeContext(ExperimentInput input)
        {
            var previous = Context;
            Context = input.Context;
            bool taskChanged = previous.Task != Context.Task || previous.Phase != Context.Phase;
            if (taskChanged) Invalidate("task_or_phase_changed");
            Invalidate(Context.MeasurementBlockReason);
            if (taskChanged) CancelAll("task_or_phase_changed");
            else if (Context.FeedbackBlockReason != null) CancelAll(Context.FeedbackBlockReason);
            output.SetGaugeVisible(GaugeVisible);
            Record("context_changed", sourceTime: input.TimestampSeconds);
        }

        private void StartContact(ExperimentInput input)
        {
            if (contactOpen)
            {
                Invalidate("duplicate_contact");
                Record("input_rejected", reason: "duplicate_contact", sourceTime: input.TimestampSeconds);
                return;
            }
            CancelPending("next_contact_before_start");
            // An already started presentation deliberately continues across this contact.
            contactOpen = true;
            strokeId++;
            invalidStrokeReason = Context.MeasurementBlockReason;
            Record("contact_started", reason: invalidStrokeReason, sourceTime: input.TimestampSeconds);
        }

        private void Release(ExperimentInput input)
        {
            if (!contactOpen)
            {
                Record("input_rejected", reason: "release_without_contact", sourceTime: input.TimestampSeconds,
                    angle: input.AngleDegrees);
                return;
            }
            contactOpen = false;
            Record("released", sourceTime: input.TimestampSeconds, angle: input.AngleDegrees);
            string reason = invalidStrokeReason ?? Context.MeasurementBlockReason;
            if (!FeedbackProtocol.IsFinite(input.AngleDegrees) || input.AngleDegrees <= 0) reason = "invalid_angle";
            invalidStrokeReason = null;
            if (reason != null)
            {
                Record("stroke_invalid", reason: reason, angle: input.AngleDegrees);
                return;
            }
            var candidate = new Candidate
            {
                Stroke = strokeId, Angle = input.AngleDegrees,
                State = FeedbackProtocol.Classify(input.AngleDegrees, target)
            };
            Record("stroke_classified", candidate);
            reason = group == StudyGroup.NoFeedback ? "no_feedback_group" : Context.FeedbackBlockReason;
            if (reason != null)
            {
                Record("feedback_suppressed", candidate, reason);
                return;
            }
            CancelPending("replaced_by_latest");
            EndActive("replaced_by_latest");
            pending = candidate;
            Record("feedback_requested", candidate);
        }

        private void Invalidate(string reason)
        {
            if (contactOpen && reason != null && invalidStrokeReason == null) invalidStrokeReason = reason;
        }

        private void CancelPending(string reason)
        {
            if (pending == null) return;
            var cancelled = pending;
            pending = null;
            Record("feedback_cancelled", cancelled, reason);
        }

        private void EndActive(string reason)
        {
            if (active == null) return;
            var ended = active;
            active = null;
            output.Stop();
            Record("feedback_software_ended", ended, reason);
        }

        private void CancelAll(string reason)
        {
            CancelPending(reason);
            EndActive(reason);
        }

        private void Record(string name, Candidate candidate = null, string reason = null,
            double? sourceTime = null, double? angle = null, double? dspTime = null)
        {
            var record = new ExperimentRecord
            {
                Sequence = ++sequence, TimeSeconds = clock.Now, InputTimeSeconds = sourceTime,
                Event = name, StrokeId = candidate?.Stroke ?? strokeId,
                AngleDegrees = candidate?.Angle ?? angle, Feedback = candidate?.State, Reason = reason,
                Group = group, Context = Context, AudioDspTimeSeconds = dspTime
            };
            log.Write(record);
            Recorded?.Invoke(record);
        }

        public void EndSession()
        {
            Guard(() =>
            {
                if (contactOpen) Record("stroke_invalid", reason: "session_ended_during_contact");
                contactOpen = false;
                CancelAll("session_ended");
                output.Stop();
                output.SetGaugeVisible(false);
                Record("session_completed");
                log.Complete(clock.Now);
                State = SessionState.Completed;
            });
        }

        private void Guard(Action action)
        {
            if (State != SessionState.Running || disposed) return;
            try { action(); }
            catch (Exception exception)
            {
                State = SessionState.Faulted;
                Fault = exception.Message;
                pending = null;
                active = null;
                contactOpen = false;
                try { output.Stop(); } catch { /* Preserve the original fault. */ }
                try { output.SetGaugeVisible(false); } catch { }
                try { log.Dispose(); } catch { }
            }
        }

        // Disposal/quit is an interruption. Only explicit EndSession writes a completion marker.
        public void Dispose()
        {
            if (disposed) return;
            Guard(() =>
            {
                CancelAll("session_interrupted");
                output.Stop();
                output.SetGaugeVisible(false);
                Record("session_interrupted");
                log.Flush();
                log.Dispose();
            });
            disposed = true;
        }
    }
}
