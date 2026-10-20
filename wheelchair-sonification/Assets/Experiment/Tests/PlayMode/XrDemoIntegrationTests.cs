using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Tests
{
    public sealed class XrDemoIntegrationTests
    {
        private DemoController app;
        private FocusVisionDemo xr;
        private string directory;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("FeedbackDemoXR", LoadSceneMode.Single);
            yield return null;
            app = UnityEngine.Object.FindAnyObjectByType<DemoController>();
            xr = app.GetComponent<FocusVisionDemo>();
            directory = Path.Combine(Path.GetTempPath(), "focus-vision-play-" + Guid.NewGuid().ToString("N"));
            app.SaveRootOverride = directory;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(app.gameObject);
            yield return null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator XrSceneStartsSilentWithOneCameraAndWorldSpaceUi()
        {
            Assert.That(app.PresentationReady, Is.False);
            Assert.That(app.Running, Is.False);
            Assert.That(app.Output.AudioSource.isPlaying, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.False);
            Assert.That(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            foreach (var canvas in app.GetComponentsInChildren<Canvas>(true))
            {
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace), canvas.name);
                Assert.That(canvas.worldCamera, Is.Not.Null);
            }
            Assert.That(app.GetComponentInChildren<EventSystem>().GetComponents<BaseInputModule>().Count(module => module.enabled), Is.EqualTo(1));
            Assert.That(app.StartSession(), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuickButtonsUseCommonClassificationAndKeepAudioAndVisualExclusive()
        {
            // Explicit fixture readiness is not a claim that an HMD was tested.
            app.SetPresentationReady(true, "test fixture");
            foreach (var group in new[] { StudyGroup.Visual, StudyGroup.Auditory, StudyGroup.NoFeedback })
            {
                foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
                {
                    Assert.That(app.QuickPush(group, 0.1f, state), Is.True);
                    yield return null; yield return null;
                    Assert.That(app.View.Gauge.Marker, Is.EqualTo(group == StudyGroup.Visual ? (FeedbackState?)state : null));
                    Assert.That(app.Output.AudioSource.isPlaying, Is.EqualTo(group == StudyGroup.Auditory));
                    yield return new WaitForSecondsRealtime(0.34f);
                    Assert.That(app.View.Gauge.Marker, Is.Null);
                }
                string log = File.ReadAllText(Path.Combine(app.SessionDirectory, "session.json"));
                Assert.That(log, Does.Contain("focus-vision-openxr"));
                Assert.That(log, Does.Contain("quick"));
                app.EndSession();
            }
        }

        [UnityTest]
        public IEnumerator NumericPadCommitsOrCancelsWithoutASystemKeyboard()
        {
            xr.ShowMode(true);
            var field = app.View.BaselineFirst;
            xr.NumberPad.Open(field);
            xr.NumberPad.Press("clear");
            foreach (char key in "64.9,70,75.1") xr.NumberPad.Press(key.ToString());
            Assert.That(field.text, Is.EqualTo("60, 60"));
            xr.NumberPad.Commit();
            Assert.That(field.text, Is.EqualTo("64.9,70,75.1"));
            xr.NumberPad.Open(field);
            xr.NumberPad.Press("clear");
            xr.NumberPad.Press("1");
            xr.NumberPad.Cancel();
            Assert.That(field.text, Is.EqualTo("64.9,70,75.1"));
            Assert.That(xr.NumberPad.Root.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DropdownListsAndHiddenPanelRemainUsableInWorldSpace()
        {
            xr.ShowMode(true);
            yield return null; // Dropdown.Start initializes its fade runner on first activation.
            app.View.Group.Show();
            yield return null; yield return null;
            var list = app.View.Group.transform.Find("Dropdown List");
            Assert.That(list.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(list.GetComponent<Canvas>().worldCamera, Is.Not.Null);
            app.View.Group.Hide();
            xr.TogglePanel();
            Assert.That(app.View.OperatorPanel.activeSelf, Is.False);
            xr.TogglePanel();
            Assert.That(app.View.OperatorPanel.activeSelf, Is.True);
            xr.Recenter();
            Assert.That(app.View.OperatorPanel.GetComponent<Canvas>().worldCamera, Is.Not.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LostReadinessStopsFeedbackAndLeavesAnIncompleteSessionWithoutAutoResume()
        {
            app.SetPresentationReady(true, "test fixture");
            app.QuickPush(StudyGroup.Visual, 0.1f, FeedbackState.Target);
            yield return null; yield return null;
            string previous = app.SessionDirectory;
            app.SetPresentationReady(false, "head_tracking_lost");
            Assert.That(app.Running, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.False);
            Assert.That(app.Output.AudioSource.isPlaying, Is.False);
            Assert.That(File.Exists(Path.Combine(previous, "completed.json")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(previous, "events.csv")), Does.Contain("head_tracking_lost"));
            Assert.That(app.StartSession(), Is.False);
            app.SetPresentationReady(true, "test fixture restored");
            yield return null;
            Assert.That(app.Running, Is.False);
            app.QuickPush(StudyGroup.Visual, 0.1f, FeedbackState.Short);
            Assert.That(app.SessionDirectory, Is.Not.EqualTo(previous));
            Assert.That(FileSessionLog.CountIncomplete(directory), Is.EqualTo(2));
            app.EndSession();
        }
    }
}
