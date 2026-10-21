using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Tests
{
    public sealed class VisitHostIntegrationTests
    {
        private FocusVisionDemo xr;
        private string root;
        [UnitySetUp] public IEnumerator SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "visit-host-" + Guid.NewGuid().ToString("N"));
            yield return Load();
        }
        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("FeedbackDemoXR", LoadSceneMode.Single); yield return null;
            xr = UnityEngine.Object.FindAnyObjectByType<FocusVisionDemo>();
            xr.App.SaveRootOverride = Path.Combine(root, "DemoSessions");
            xr.Research.RootOverride = Path.Combine(root, "ResearchSessions");
            xr.VisitHost.RootOverride = Path.Combine(root, "ResearchVisits");
            xr.EnableEditorPreview(false); xr.VisitHost.SetInput("synthetic");
            yield return null; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        { UnityEngine.Object.Destroy(xr.gameObject); yield return null; if (Directory.Exists(root)) Directory.Delete(root, true); }
        private OperatorResult Command(string action, string value = "", double number = 0, string id = null, int? revision = null)
        {
            xr.VisitHost.ObserveOperator(true);
            return xr.VisitHost.Execute(new OperatorCommand { id = id ?? Guid.NewGuid().ToString("N"), visitId = xr.VisitHost.Visit?.State.visitId ?? "",
                revision = revision ?? xr.VisitHost.Visit?.State.revision ?? 0, action = action, value = value, number = number });
        }
        [UnityTest] public IEnumerator CommandsAreDurableIdempotentAndStaleStartsAreRejected()
        {
            Assert.That(Command("createVisit", "1", 1, "create-one").ok, Is.True);
            string visit = xr.VisitHost.Visit.State.visitId;
            Assert.That(Command("createVisit", "1", 1, "create-one").ok, Is.True);
            Assert.That(xr.VisitHost.Visit.State.visitId, Is.EqualTo(visit));
            Assert.That(Command("start", revision: -1).ok, Is.False);
            Assert.That(xr.VisitHost.Visit.Current.id, Is.EqualTo("consent"));
            Assert.That(Command("start").ok, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(xr.VisitHost.VisitRoot, visit, "visit.json")), Does.Contain("create-one"));
            yield return null;
            Assert.That(xr.Panel.Visible, Is.False);
        }
        [UnityTest] public IEnumerator ParticipantUsesNoControllerAndAbortDoesNotBecomeCompletion()
        {
            Command("createVisit", "1", 1); Command("start"); yield return null; Command("start");
            Assert.That(Command("start").ok, Is.True);
            string trial = xr.Research.SessionDirectory;
            Assert.That(xr.Research.State, Is.EqualTo(TrialRunState.Countdown));
            yield return null; Assert.That(xr.Panel.Visible, Is.False); Assert.That(xr.WaitingLabel.gameObject.activeInHierarchy, Is.False);
            Assert.That(Command("abort", "video_failure").ok, Is.True);
            Assert.That(File.Exists(Path.Combine(trial, "completed.json")), Is.False);
            Assert.That(xr.VisitHost.Visit.State.status, Is.EqualTo("interrupted"));
            Assert.That(xr.Gauge.Visible, Is.False);
        }
        [UnityTest] public IEnumerator BrowserLeaseExpiresWithoutAnotherCommandAndStopsPendingTrial()
        {
            Command("createVisit", "1", 1); Command("start"); yield return null; Command("start"); Command("start");
            xr.VisitHost.ObserveOperator(true, .05);
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(xr.Research.Active, Is.False); Assert.That(xr.VisitHost.Visit.State.lastReason, Is.EqualTo("connection_lost"));
            xr.VisitHost.ObserveOperator(true); yield return null;
            Assert.That(xr.Research.Active, Is.False, "Connection recovery is never an automatic restart.");
        }
        [UnityTest] public IEnumerator OpeningANewTrialResetsTheIdleIntervalButAnActiveGapStillStopsIt()
        {
            Command("createVisit", "1", 1); Command("start"); yield return null;
            Command("start"); yield return null;
            System.Threading.Thread.Sleep(150); // An idle pause before the recording opens.
            Assert.That(Command("start").ok, Is.True);
            yield return null;
            Assert.That(xr.Research.State, Is.EqualTo(TrialRunState.Countdown));
            System.Threading.Thread.Sleep(150); // A missing interval inside the active recording.
            yield return null;
            Assert.That(xr.Research.Active, Is.False);
            Assert.That(xr.VisitHost.Visit.State.status, Is.EqualTo("interrupted"));
        }
        [UnityTest] public IEnumerator NewVisitsDoNotReuseIdentifiersOrThePreviousPersonsMajorSide()
        {
            Assert.That(Command("createVisit", "1", 1).ok, Is.True);
            Assert.That(Command("setMajor", "left").ok, Is.True);
            Assert.That(xr.VisitHost.Snapshot().majorSide, Is.EqualTo("left"));
            Assert.That(Command("terminate", "fixture_end").ok, Is.True);
            Assert.That(Command("createVisit", "2", 1).ok, Is.False);
            Assert.That(Command("createVisit", "1", 2).ok, Is.False);
            Assert.That(Command("createVisit", "2", 2).ok, Is.True);
            Assert.That(xr.Research.MajorRight, Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator MissingBaselineCannotBeConvertedIntoTargetOrAllocation()
        {
            Command("createVisit", "1", 1);
            xr.VisitHost.Visit.State.step = Array.FindIndex(VisitProtocol.Steps, s => s.id == "target");
            yield return null;
            Assert.That(Command("start").ok, Is.False); Assert.That(Command("assignGroup", "Visual").ok, Is.False);
            Assert.That(xr.Research.GroupAssigned, Is.False);
        }
        [UnityTest] public IEnumerator RestartPreservesUnmeasuredBaselinesAndInvalidatesTheRealCalibration()
        {
            Command("createVisit", "1", 1); string visit = xr.VisitHost.Visit.State.visitId;
            yield return Load();
            Assert.That(Command("resumeVisit").ok, Is.True);
            Assert.That(xr.VisitHost.Visit.State.visitId, Is.EqualTo(visit));
            Assert.That(xr.Research.BaselineFirst, Is.Null); Assert.That(xr.Research.BaselineSecond, Is.Null);
            Assert.That(xr.VisitHost.Visit.State.recoveryRequired, Is.True);
            Assert.That(xr.Research.SourceKind, Is.EqualTo("synthetic_pose_fixture"), "An explicitly saved synthetic visit stays synthetic.");
            Assert.That(xr.Research.Active, Is.False);
        }
        [UnityTest] public IEnumerator FailedStorageCannotContinueAVisit()
        {
            Directory.CreateDirectory(root); string file = Path.Combine(root, "not-a-directory"); File.WriteAllText(file, "fixture");
            xr.VisitHost.RootOverride = file;
            Assert.That(Command("createVisit", "1", 1).ok, Is.False); yield return null;
            Assert.That(xr.VisitHost.Snapshot().saveHealthy, Is.False);
            Assert.That(xr.Research.Active, Is.False); Assert.That(xr.Gauge.Visible, Is.False);
            xr.VisitHost.RootOverride = Path.Combine(root, "ResearchVisits");
        }
    }
}
