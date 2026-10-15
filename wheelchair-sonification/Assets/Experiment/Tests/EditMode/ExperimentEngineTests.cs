using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WheelchairSonification.Core;

namespace WheelchairSonification.Tests
{
    internal sealed class TestClock : IClock { public double Now { get; set; } }
    internal sealed class TestOutput : IFeedbackOutput
    {
        public readonly List<FeedbackState> Starts = new List<FeedbackState>();
        public bool Playing;
        public bool Gauge;
        public double? Begin(StudyGroup group, FeedbackState state) { Starts.Add(state); Playing = true; return null; }
        public void Stop() { Playing = false; }
        public void SetGaugeVisible(bool visible) { Gauge = visible; }
    }
    internal sealed class TestLog : IExperimentLog
    {
        public readonly List<ExperimentRecord> Records = new List<ExperimentRecord>();
        public bool FailWrite, FailFlush, FailComplete, Completed, Disposed;
        public void Write(ExperimentRecord record)
        {
            if (FailWrite) throw new IOException("Injected write failure");
            Records.Add(record);
        }
        public void Flush() { if (FailFlush) throw new IOException("Injected flush failure"); }
        public void Complete(double time)
        {
            if (FailComplete) throw new IOException("Injected completion failure");
            Completed = true;
        }
        public void Dispose() { Disposed = true; }
    }

    public class ExperimentEngineTests
    {
        private TestClock clock;
        private TestOutput output;
        private TestLog log;
        private ExperimentEngine engine;
        internal static ExperimentContext Training(bool central = true, bool tracking = true,
            MotionState motion = MotionState.Straight) => new ExperimentContext(StudyPhase.Training,
                StudyTask.Straight, motion, central, tracking);

        [SetUp]
        public void SetUp() { Create(StudyGroup.Visual); }
        private void Create(StudyGroup group)
        {
            engine?.Dispose();
            clock = new TestClock(); output = new TestOutput(); log = new TestLog();
            engine = new ExperimentEngine(clock, output, log, group, 70);
        }
        [TearDown] public void TearDown() { engine.Dispose(); }
        private void Context(ExperimentContext context) => engine.Process(ExperimentInput.ChangeContext(clock.Now, context));
        private void Contact() => engine.Process(ExperimentInput.Contact(clock.Now));
        private void Release(double angle = 70) => engine.Process(ExperimentInput.Release(clock.Now, angle));
        private void Stroke(double angle = 70) { Contact(); Release(angle); }

        [Test]
        public void InitialStateHasNoFeedbackOrGauge()
        {
            Assert.That(engine.Context.Phase, Is.EqualTo(StudyPhase.Preparation));
            Stroke(); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Assert.That(output.Gauge, Is.False);
        }

        [TestCase(StudyGroup.Auditory, 1, false)]
        [TestCase(StudyGroup.Visual, 1, true)]
        [TestCase(StudyGroup.NoFeedback, 0, false)]
        public void GroupsReceiveOnlyTheirAssignedFeedback(StudyGroup group, int starts, bool gauge)
        {
            Create(group); Context(Training()); Stroke();
            Assert.That(output.Starts, Is.Empty, "Release requests feedback; Tick dispatches it separately.");
            engine.Tick();
            Assert.That(output.Starts.Count, Is.EqualTo(starts));
            Assert.That(output.Gauge, Is.EqualTo(gauge));
            Assert.That(log.Records.Count(r => r.Event == "stroke_classified"), Is.EqualTo(1));
        }

        [Test]
        public void AllNonTrainingPhasesAndNonStraightTasksSuppressFeedback()
        {
            foreach (StudyPhase phase in Enum.GetValues(typeof(StudyPhase)))
            foreach (StudyTask task in Enum.GetValues(typeof(StudyTask)))
            {
                if (phase == StudyPhase.Training && task == StudyTask.Straight) continue;
                Context(new ExperimentContext(phase, task, MotionState.Straight, true, true));
                Stroke(); engine.Tick();
                Assert.That(output.Starts, Is.Empty, phase + "/" + task);
                Assert.That(output.Gauge, Is.False, phase + "/" + task);
            }
        }

        [Test]
        public void ContactBeforeDispatchCancelsPendingFeedback()
        {
            Context(Training()); Stroke();
            Assert.That(engine.HasPendingFeedback, Is.True);
            Contact(); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Assert.That(log.Records.Any(r => r.Event == "feedback_cancelled" && r.Reason == "next_contact_before_start"), Is.True);
        }

        [Test]
        public void StartedFeedbackContinuesAcrossNextContactAndEndsAt300Milliseconds()
        {
            Context(Training()); clock.Now = 2; Stroke(); engine.Tick();
            clock.Now = 2.1; Contact(); engine.Tick();
            Assert.That(output.Playing, Is.True);
            clock.Now = 2.2999; engine.Tick();
            Assert.That(output.Playing, Is.True);
            clock.Now = 2.3; engine.Tick();
            Assert.That(output.Playing, Is.False);
            Assert.That(log.Records.Last().Reason, Is.EqualTo("duration_elapsed"));
        }

        [Test]
        public void LatestValidReleaseInterruptsAndStartsANew300Milliseconds()
        {
            Context(Training()); Stroke(60); engine.Tick();
            clock.Now = 0.1; Contact(); Release(80);
            Assert.That(output.Playing, Is.False);
            engine.Tick();
            Assert.That(output.Starts, Is.EqualTo(new[] { FeedbackState.Short, FeedbackState.Long }));
            clock.Now = 0.3999; engine.Tick(); Assert.That(output.Playing, Is.True);
            clock.Now = 0.4; engine.Tick(); Assert.That(output.Playing, Is.False);
            Assert.That(log.Records.Any(r => r.Event == "feedback_software_ended" && r.Reason == "replaced_by_latest"), Is.True);
        }

        [TestCase("area")]
        [TestCase("tracking")]
        [TestCase("stopped")]
        [TestCase("braking")]
        [TestCase("turning")]
        [TestCase("task")]
        [TestCase("phase")]
        public void InvalidContextCancelsPendingAndEndsActiveFeedback(string change)
        {
            ExperimentContext invalid;
            switch (change)
            {
                case "area": invalid = Training(central: false); break;
                case "tracking": invalid = Training(tracking: false); break;
                case "stopped": invalid = Training(motion: MotionState.Stopped); break;
                case "braking": invalid = Training(motion: MotionState.Braking); break;
                case "turning": invalid = Training(motion: MotionState.Turning); break;
                case "task": invalid = new ExperimentContext(StudyPhase.Training, StudyTask.FigureEight, MotionState.Straight, true, true); break;
                default: invalid = new ExperimentContext(StudyPhase.ImmediateAssessment, StudyTask.Straight, MotionState.Straight, true, true); break;
            }
            Context(Training()); Stroke(); Context(invalid); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Context(Training()); Stroke(); engine.Tick(); Assert.That(output.Playing, Is.True);
            Context(invalid); Assert.That(output.Playing, Is.False);
            Assert.That(engine.HasPendingFeedback, Is.False);
        }

        [Test]
        public void TrackingLossAndRecoveryCannotJoinAnInterruptedStroke()
        {
            Context(Training()); Contact();
            Context(Training(tracking: false)); Context(Training()); Release(); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Assert.That(log.Records.Any(r => r.Event == "stroke_invalid" && r.Reason == "tracking_invalid"), Is.True);
            Stroke(); engine.Tick(); Assert.That(output.Starts.Count, Is.EqualTo(1));
        }

        [Test]
        public void StrokeBeginningOutsideAreaStaysInvalidAfterEntering()
        {
            Context(Training(central: false)); Contact(); Context(Training()); Release(); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Assert.That(log.Records.Any(r => r.Event == "stroke_invalid" && r.Reason == "outside_central_area"), Is.True);
        }

        [Test]
        public void LeavingAndReenteringOrChangingPhaseMidStrokeInvalidatesIt()
        {
            Context(Training()); Contact(); Context(Training(central: false)); Context(Training()); Release(); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Contact(); Context(new ExperimentContext(StudyPhase.Baseline, StudyTask.Straight, MotionState.Straight, true, true));
            Context(Training()); Release(); engine.Tick(); Assert.That(output.Starts, Is.Empty);
        }

        [Test]
        public void InvalidAnglesAndOutOfOrderEventsNeverProduceAState()
        {
            Context(Training());
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
            { Stroke(value); engine.Tick(); }
            clock.Now = 1; Contact();
            engine.Process(ExperimentInput.Release(0, 70)); engine.Tick();
            Assert.That(output.Starts, Is.Empty);
            Assert.That(log.Records.Any(r => r.Event == "stroke_classified"), Is.False);
        }

        [TestCase("write")]
        [TestCase("flush")]
        [TestCase("complete")]
        public void StorageFailureStopsTheSessionAndCannotMarkItComplete(string failure)
        {
            Context(Training()); Stroke(); engine.Tick();
            Assert.That(output.Playing, Is.True);
            if (failure == "write") { log.FailWrite = true; Contact(); }
            if (failure == "flush") { log.FailFlush = true; clock.Now = 0.6; engine.Tick(); }
            if (failure == "complete") { log.FailComplete = true; engine.EndSession(); }
            Assert.That(engine.State, Is.EqualTo(SessionState.Faulted));
            Assert.That(output.Playing || output.Gauge, Is.False);
            Assert.That(log.Completed, Is.False);
            Assert.That(log.Disposed, Is.True);
            Stroke(); engine.Tick(); Assert.That(output.Starts.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitEndCompletesButDisposalLeavesAnIncompleteSession()
        {
            Context(Training()); Stroke(); engine.Tick(); engine.EndSession();
            Assert.That(log.Completed, Is.True);
            Assert.That(engine.State, Is.EqualTo(SessionState.Completed));
            Assert.That(output.Playing || output.Gauge, Is.False);
            Create(StudyGroup.Visual); engine.Dispose();
            Assert.That(log.Completed, Is.False);
            Assert.That(log.Disposed, Is.True);
        }

        [Test]
        public void FixedEventAndClockSequenceIsReproducibleAcrossSessions()
        {
            string Run()
            {
                Create(StudyGroup.Visual); Context(Training());
                for (int i = 0; i < 100; i++)
                {
                    clock.Now = i * 60; Stroke(i % 2 == 0 ? 60 : 80); engine.Tick();
                    clock.Now += 0.5; engine.Tick();
                }
                engine.EndSession();
                return string.Join("\n", log.Records.Select(r => $"{r.Sequence}|{r.TimeSeconds:R}|{r.Event}|{r.StrokeId}|{r.Feedback}|{r.Reason}"));
            }
            string first = Run();
            Assert.That(Run(), Is.EqualTo(first));
            Assert.That(clock.Now, Is.GreaterThan(90 * 60), "Accelerated logical time, not a physical endurance test");
        }
    }
}
