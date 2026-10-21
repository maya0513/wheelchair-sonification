using System;
using System.Linq;
using NUnit.Framework;
using WheelchairSonification.Core;

namespace WheelchairSonification.Tests
{
    public sealed class VisitProtocolTests
    {
        private sealed class Clock : IClock { public double Now { get; set; } }
        private Clock clock;
        private VisitEngine engine;
        [SetUp] public void Setup() { clock = new Clock(); engine = new VisitEngine(clock, new VisitState { visitId = "fixture" }); }
        private void At(string id) { engine.State.step = Array.FindIndex(VisitProtocol.Steps, s => s.id == id); engine.State.status = "ready"; }
        [Test] public void ScheduleHasTwelveEvaluationsAndNineteenMinuteTrainingWithoutExtraTasks()
        {
            var steps = VisitProtocol.Steps;
            Assert.That(steps.Where(s => s.kind == "trial" && s.phase != StudyPhase.Training && s.phase != StudyPhase.Preparation).Count(), Is.EqualTo(12));
            Assert.That(steps.Where(s => s.phase == StudyPhase.Training).Sum(s => s.seconds), Is.EqualTo(720));
            Assert.That(steps.Where(s => s.kind == "interval").Sum(s => s.seconds), Is.EqualTo(420));
            Assert.That(steps.Single(s => s.id == "rest").seconds, Is.EqualTo(1200));
            Assert.That(steps.Where(s => s.phase == StudyPhase.Baseline).Select(s => s.task), Is.EqualTo(new[] { StudyTask.FigureEight, StudyTask.FigureEight, StudyTask.Straight, StudyTask.Straight }));
        }
        [Test] public void BaselineAndComprehensionAreRequiredBeforeOneTimeAllocation()
        {
            At("target"); Assert.Throws<InvalidOperationException>(() => engine.StartStep());
            engine.State.targetReady = true; engine.StartStep();
            At("allocation"); Assert.Throws<InvalidOperationException>(() => engine.Assign(StudyGroup.Visual));
            engine.State.comprehensionPassed = true; engine.Assign(StudyGroup.Visual);
            Assert.That(engine.Current.id, Is.EqualTo("train-straight-1"));
            Assert.Throws<InvalidOperationException>(() => engine.Assign(StudyGroup.Auditory));
        }
        [Test] public void TwentyMinuteRestCannotBeShortenedAndRequiresExplicitNextStart()
        {
            At("rest"); engine.StartStep(); clock.Now = 1199.9; engine.Tick();
            Assert.That(engine.State.status, Is.EqualTo("timer"));
            Assert.Throws<InvalidOperationException>(() => engine.StartStep());
            clock.Now = 1200; engine.Tick(); Assert.That(engine.Current.id, Is.EqualTo("rest"));
            engine.StartStep(); Assert.That(engine.Current.id, Does.StartWith("retention-"));
        }
        [Test] public void InterruptedRestRequiresExternalEvidenceAndNeverResumesAutomatically()
        {
            At("rest"); engine.StartStep(); clock.Now = 600; engine.Tick(); engine.Interrupt("application_restart"); engine.Recover();
            Assert.Throws<InvalidOperationException>(() => engine.StartStep());
            Assert.Throws<InvalidOperationException>(() => engine.VerifyRest(1199));
            engine.VerifyRest(1201); engine.StartStep(); Assert.That(engine.Current.id, Does.StartWith("retention-"));
        }
        [Test] public void OnlyOneTechnicalReplacementPerEvaluationPhase()
        {
            At("pre-FigureEight-1"); engine.StartStep(); engine.State.activeTrialId = "first"; engine.Interrupt("video_failure");
            engine.Recover(); engine.Retry(); Assert.That(engine.State.technicalRetries[0], Is.EqualTo(1));
            engine.StartStep(); engine.FinishTrial("replacement");
            Assert.That(engine.State.trials.Last().replacesTrialId, Is.EqualTo("first"));
            engine.Interrupt("video_failure"); engine.Recover(); Assert.Throws<InvalidOperationException>(() => engine.Retry());
        }
        [TestCase("pain")][TestCase("participant_stop")][TestCase("safety_intervention")][TestCase("visit_time_limit")][TestCase("elapsed_time_unverified")]
        public void SafetyStopsCannotBeRecoveredOrSkipped(string reason)
        { engine.Interrupt(reason); Assert.Throws<InvalidOperationException>(() => engine.Recover()); Assert.Throws<InvalidOperationException>(() => engine.Skip()); }
        [Test] public void PoorPerformanceAndTrainingDoNotPermitTechnicalExtraTrials()
        {
            At("pre-FigureEight-1"); engine.Interrupt("path_error"); engine.Recover(); Assert.Throws<InvalidOperationException>(() => engine.Retry());
            At("train-straight-1"); engine.Interrupt("video_failure"); engine.Recover(); Assert.Throws<InvalidOperationException>(() => engine.Retry());
        }
        [Test] public void ComprehensionUsesBothModalitiesBeforePassingAndKeepsVerbalAnswers()
        {
            At("comprehension");
            for (int m = 0; m < 2; m++)
            {
                engine.StartStep();
                for (int i = 0; i < 10; i++)
                {
                    clock.Now += 1.01; engine.Tick(); int error = engine.QuizError;
                    engine.Answer(error < -5 ? "Short" : error > 5 ? "Long" : "Target", error < -5 ? "longer" : error > 5 ? "shorter" : "maintain");
                }
                Assert.That(engine.State.comprehensionPassed, Is.EqualTo(m == 1));
            }
            Assert.That(engine.State.quiz.answers.Count, Is.EqualTo(20));
            Assert.That(engine.State.quiz.answers.Select(a => a.modality).Distinct().Count(), Is.EqualTo(2));
            engine.StartStep(); Assert.That(engine.Current.kind, Is.EqualTo("allocation"));
        }
        [Test] public void FailedSecondQuizAttemptBlocksAllocationAndTimeoutsAreRecorded()
        {
            At("comprehension");
            for (int a = 0; a < 2; a++)
            {
                engine.StartStep();
                for (int q = 0; q < 10; q++) { clock.Now += 1.1; engine.Tick(); clock.Now += 5.1; engine.Tick(); }
            }
            Assert.That(engine.State.quiz.failed, Is.True); Assert.That(engine.State.comprehensionPassed, Is.False);
            Assert.That(engine.State.quiz.answers.All(a => a.timeout), Is.True);
            Assert.Throws<InvalidOperationException>(() => engine.StartStep());
        }
        [Test] public void EvaluationTurnaroundCannotBeSkippedByReconnecting()
        {
            At("pre-FigureEight-1"); engine.StartStep(); engine.FinishTrial("one");
            Assert.That(engine.State.status, Is.EqualTo("turnaround"));
            clock.Now = 29; engine.Tick(); engine.Interrupt("connection_lost"); engine.Recover();
            Assert.Throws<InvalidOperationException>(() => engine.StartStep());
            clock.Now = 30; engine.Tick(); Assert.That(engine.State.status, Is.EqualTo("ready"));
        }
        [Test] public void QuestionnaireRequiresAnAnswerOrRecordedMissingReason()
        {
            At("questionnaire"); Assert.Throws<InvalidOperationException>(() => engine.StartStep());
            engine.State.questionnaireRecorded = true; engine.StartStep(); Assert.That(engine.Current.kind, Is.EqualTo("rest"));
        }
        [Test] public void GenericStopNeedsAReviewedReasonBeforeRecovery()
        {
            At("pre-FigureEight-1"); engine.Interrupt("operator_stop");
            Assert.Throws<InvalidOperationException>(() => engine.Recover());
            engine.ReviewStopReason("video_failure"); engine.Recover(); Assert.That(engine.CanRetry, Is.True);
        }
        [Test] public void AReviewedReasonCannotDowngradeSafetyOrClockStops()
        {
            engine.Interrupt("pain"); Assert.Throws<InvalidOperationException>(() => engine.ReviewStopReason("video_failure"));
            Assert.That(engine.State.lastReason, Is.EqualTo("pain"));
        }
        [Test] public void NinetyMinuteLimitInterruptsInsteadOfShrinkingRest()
        { At("rest"); engine.StartStep(); clock.Now = 5401; engine.Tick(); Assert.That(engine.State.lastReason, Is.EqualTo("visit_time_limit")); }
    }
}
