using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Tests
{
    public sealed class XrDemoIntegrationTests
    {
        private FocusVisionDemo xr;
        private string directory;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("FeedbackDemoXR", LoadSceneMode.Single);
            yield return null;
            xr = UnityEngine.Object.FindAnyObjectByType<FocusVisionDemo>();
            directory = Path.Combine(Path.GetTempPath(), "focus-preview-" + Guid.NewGuid().ToString("N"));
            xr.App.SaveRootOverride = Path.Combine(directory, "demo");
            xr.Research.RootOverride = Path.Combine(directory, "research");
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(xr.gameObject);
            yield return null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        [UnityTest]
        public IEnumerator StartsSilentWithoutParticipantControlsOrHeadLockedBar()
        {
            Assert.That(xr.Ready, Is.False);
            Assert.That(xr.Panel.Visible, Is.False);
            Assert.That(xr.TechnicianPreview, Is.False);
            Assert.That(xr.Gauge.Visible, Is.False);
            Assert.That(xr.App.Output.AudioSource.isPlaying, Is.False);
            Assert.That(xr.App.View.OperatorPanel.activeSelf, Is.False);
            Assert.That(xr.App.View.ParticipantCanvas.gameObject.activeSelf, Is.False);
            Assert.That(xr.Panel.Root.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(xr.Panel.Root.IsChildOf(xr.Rig.Camera.transform), Is.False);
            Assert.That(xr.transform.Find("XR controls"), Is.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            yield return null;
        }
        [UnityTest]
        public IEnumerator AllPagesUseLargeSdfTextAndSingleVisiblePage()
        {
            xr.EnableEditorPreview();
            foreach (XrPage page in Enum.GetValues(typeof(XrPage)))
            {
                xr.Panel.Show(page); yield return null;
                var labels = xr.Panel.Root.GetComponentsInChildren<TMP_Text>();
                Assert.That(labels.Length, Is.GreaterThan(3));
                foreach (var label in labels)
                { Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(36), label.name); Assert.That(label.enableAutoSizing, Is.False); }
            }
        }
        [UnityTest]
        public IEnumerator QuickFeedbackHidesControlsAndKeepsModalitiesExclusive()
        {
            xr.EnableEditorPreview();
            foreach (StudyGroup group in Enum.GetValues(typeof(StudyGroup)))
            foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
            {
                xr.RunQuick(group, .1f, state); yield return null; yield return null;
                Assert.That(xr.Panel.Visible, Is.False);
                Assert.That(xr.Gauge.Marker, Is.EqualTo(group == StudyGroup.Visual ? (FeedbackState?)state : null));
                Assert.That(xr.App.Output.AudioSource.isPlaying, Is.EqualTo(group == StudyGroup.Auditory));
                yield return new WaitForSecondsRealtime(.34f);
                Assert.That(xr.Gauge.Marker, Is.Null);
                xr.StopQuick(); xr.Panel.Show(XrPage.Quick);
            }
        }
        [UnityTest]
        public IEnumerator QuickReturnsAfterOneSecondAndClearsGauge()
        {
            xr.EnableEditorPreview(); xr.RunQuick(StudyGroup.Visual, .1f, FeedbackState.Target);
            yield return new WaitForSecondsRealtime(1.08f);
            Assert.That(xr.Panel.Visible, Is.True); Assert.That(xr.Gauge.Visible, Is.False); Assert.That(xr.App.Running, Is.False);
        }
        [UnityTest]
        public IEnumerator NumericPadBuffersCancelsAndCommitsWithoutNativeInputField()
        {
            xr.EnableEditorPreview();
            string value = "0.10";
            xr.Panel.OpenNumber("音量", value, next => value = next);
            xr.Panel.PressNumber("backspace"); xr.Panel.PressNumber("5");
            Assert.That(value, Is.EqualTo("0.10"));
            xr.Panel.CancelNumber(); Assert.That(value, Is.EqualTo("0.10"));
            xr.Panel.OpenNumber("音量", value, next => value = next);
            xr.Panel.PressNumber("backspace"); xr.Panel.PressNumber("5"); xr.Panel.CommitNumber();
            Assert.That(value, Is.EqualTo("0.15"));
            Assert.That(xr.Panel.KeypadVisible, Is.False);
            yield return null;
            Assert.That(xr.Panel.Root.GetComponentsInChildren<UnityEngine.UI.InputField>().Length, Is.Zero);
        }
        [UnityTest]
        public IEnumerator ExportLocationIsShownInFullOnTheSamePanel()
        {
            xr.EnableEditorPreview();
            xr.Panel.Show(XrPage.Records, 1); yield return null;
            var label = xr.Panel.Root.GetComponentsInChildren<TMP_Text>().Single(x => x.name == "Export destination");
            Assert.That(label.text.Replace("\n", ""), Is.EqualTo(AndroidDownloadExport.RelativeDirectory));
            label.ForceMeshUpdate(); Assert.That(label.isTextOverflowing, Is.False);
            Assert.That(xr.Gauge.Visible, Is.False);
        }
        [UnityTest]
        public IEnumerator PanelStaysInWorldAndRecenterMovesItToFront()
        {
            xr.EnableEditorPreview();
            var panel = xr.Panel.Root;
            Vector3 before = panel.position;
            xr.Rig.Camera.transform.rotation = Quaternion.Euler(0, 30, 0);
            yield return null;
            Assert.That(Vector3.Distance(panel.position, before), Is.LessThan(.0001f));
            xr.Recenter();
            Vector3 relative = xr.Rig.Camera.transform.InverseTransformPoint(panel.position);
            Assert.That(Mathf.Abs(relative.x), Is.LessThan(.01f));
            Assert.That(relative.z, Is.EqualTo(1.4f).Within(.01f));
        }
        private sealed class FivePoses : IPoseSource
        {
            public bool Valid = true;
            public string Kind => "explicit_test_fixture";
            public PoseFrame Read(double time)
            {
                var f = new PoseFrame { time = time };
                for (int i = 0; i < 5; i++) f.poses.Add(new TrackerPose { key = "t" + i, runtimeDeviceId = i + 1,
                    valid = Valid, acquiredTime = time, position = new Vector3(0, 0, 3) });
                return f;
            }
            public void Dispose() { }
        }
        [UnityTest]
        public IEnumerator RealTrialPreparationLossStopsAndPreservesIncompleteData()
        {
            xr.EnableEditorPreview(); var source = new FivePoses(); xr.Research.SetSource(source);
            var c = xr.Research.Wizard.Calibration;
            c.bodyReady = c.courseReady = true; c.courseEnd = Vector3.forward * 10;
            c.left.geometryReady = c.right.geometryReady = c.left.contactReady = c.right.contactReady = true;
            c.left.radialInTracker = c.right.radialInTracker = Vector3.up;
            for (int i = 0; i < 5; i++) { c.roles[i].key = "t" + i; c.roles[i].runtimeDeviceId = i + 1; }
            yield return null; yield return null;
            Assert.That(xr.Research.BeginTrial(.1f), Is.True);
            Assert.That(xr.Research.State, Is.EqualTo(TrialRunState.Countdown));
            string saved = xr.Research.SessionDirectory;
            yield return null; source.Valid = false; yield return null; yield return null;
            Assert.That(xr.Research.Active, Is.False);
            Assert.That(xr.Research.State, Is.EqualTo(TrialRunState.WaitingForStop));
            Assert.That(xr.Gauge.Visible, Is.False);
            Assert.That(File.Exists(Path.Combine(saved, "completed.json")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(saved, "trials.csv")), Does.Contain("tracker_tracking_lost"));
            xr.Research.ConfirmStopped();
            Assert.That(xr.Research.BeginTrial(.1f), Is.False, "Stop confirmation cannot repair tracking.");
            Assert.That(File.Exists(Path.Combine(saved, "poses.csv")), Is.True);
        }
    }
}
