using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    public class DemoIntegrationTests
    {
        private DemoController app;
        private string root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("FeedbackDemo", LoadSceneMode.Single);
            app = UnityEngine.Object.FindFirstObjectByType<DemoController>();
            Assert.That(app, Is.Not.Null);
            root = Path.Combine(Path.GetTempPath(), "sonification-play-" + Guid.NewGuid().ToString("N"));
            app.SaveRootOverride = root;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            UnityEngine.Object.Destroy(app.gameObject);
            yield return null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private void Training()
        {
            app.View.Phase.value = (int)StudyPhase.Training;
            app.View.Task.value = (int)StudyTask.Straight;
            app.View.Motion.value = (int)MotionState.Straight;
            app.View.CentralArea.isOn = true;
        }
        private void Push(string angle)
        {
            app.View.Angle.text = angle;
            app.View.ContactButton.onClick.Invoke();
            app.View.ReleaseButton.onClick.Invoke();
        }

        [UnityTest]
        public IEnumerator InitialSceneIsSilentAndJapaneseLabelsExist()
        {
            Assert.That(app.Running, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.False);
            Assert.That(app.Output.AudioSource.isPlaying, Is.False);
            Assert.That(app.View.TargetText.text, Does.Contain("目標 70°"));
            var font = Resources.Load<Font>("DemoJapanese");
            font.RequestCharactersInTexture("短い目標範囲長い70°", 19);
            foreach (char character in "短い目標範囲長い70°")
                Assert.That(font.HasCharacter(character), Is.True, "Missing glyph: " + character);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DropdownOpensAndSelectsAGroupThroughItsToggleItems()
        {
            app.View.Group.Show();
            yield return null;
            var list = app.View.Group.transform.Find("Dropdown List");
            Assert.That(list, Is.Not.Null);
            var items = list.GetComponentsInChildren<Toggle>();
            Assert.That(items.Length, Is.EqualTo(3));
            items[1].isOn = true;
            Assert.That(app.View.Group.value, Is.EqualTo((int)StudyGroup.Visual));
            app.View.Group.Hide();
            yield return new WaitForSecondsRealtime(0.2f);
        }

        [UnityTest]
        public IEnumerator VolumeIndicatorTracksItsValueAndPreviewDoesNotOverlapExplanation()
        {
            yield return null;
            foreach (float value in new[] { 0.1f, 0.75f })
            {
                app.View.Volume.value = value;
                Canvas.ForceUpdateCanvases();
                float width = app.View.Volume.GetComponent<RectTransform>().rect.width;
                Assert.That(app.View.Volume.fillRect.rect.width / width, Is.EqualTo(value).Within(0.001));
                Assert.That(app.View.Volume.handleRect.rect.height,
                    Is.LessThanOrEqualTo(app.View.Volume.GetComponent<RectTransform>().rect.height));
            }
            var gaugeCorners = new Vector3[4];
            var explanationCorners = new Vector3[4];
            app.View.Gauge.Root.GetWorldCorners(gaugeCorners);
            var explanation = app.View.OperatorPanel.transform.Find("Operator layout/Preview explanation");
            ((RectTransform)explanation).GetWorldCorners(explanationCorners);
            Assert.That(explanationCorners[0].y, Is.GreaterThan(gaugeCorners[1].y), "Operator explanation overlaps participant gauge");
        }

        [UnityTest]
        public IEnumerator VisualButtonsReachMarkerAndPersistACompletedSession()
        {
            app.View.Group.value = (int)StudyGroup.Visual;
            app.View.StartButton.onClick.Invoke(); Training(); Push("64.9");
            yield return null; yield return null;
            Assert.That(app.View.Gauge.Visible, Is.True);
            Assert.That(app.View.Gauge.Marker, Is.EqualTo(FeedbackState.Short));
            Assert.That(app.Output.AudioSource.isPlaying, Is.False);
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(0.36f);
            Assert.That(app.View.Gauge.Marker, Is.Null, "Presentation uses monotonic time rather than timeScale.");
            app.View.EndButton.onClick.Invoke();
            string csv = File.ReadAllText(Path.Combine(app.SessionDirectory, "events.csv"));
            Assert.That(csv, Does.Contain("stroke_classified"));
            Assert.That(csv, Does.Contain("feedback_software_started"));
            Assert.That(csv, Does.Contain("feedback_software_ended"));
            Assert.That(File.Exists(Path.Combine(app.SessionDirectory, "completed.json")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(app.SessionDirectory, "session.json")), Does.Contain("simulated_angle_events"));
        }

        [UnityTest]
        public IEnumerator NoFeedbackGroupAndEvaluationsNeverShowMarkers()
        {
            app.View.Group.value = (int)StudyGroup.NoFeedback;
            Assert.That(app.StartSession(), Is.True); Training(); Push("70");
            yield return null; yield return null;
            Assert.That(app.Output.AudioSource.isPlaying, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.False);
            app.EndSession();
            app.View.Group.value = (int)StudyGroup.Visual;
            Assert.That(app.StartSession(), Is.True); Training();
            app.View.Phase.value = (int)StudyPhase.ImmediateAssessment;
            Push("70"); yield return null; yield return null;
            Assert.That(app.View.Gauge.Visible, Is.False);
            Assert.That(app.View.Gauge.Marker, Is.Null);
            app.EndSession();
        }

        [UnityTest]
        public IEnumerator AudioClipsAreStereoAndOnlyTheAuditoryGroupStartsAudio()
        {
            foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
            {
                AudioClip clip = app.Output.Clip(state);
                Assert.That(clip.frequency, Is.EqualTo(48000));
                Assert.That(clip.channels, Is.EqualTo(2));
                Assert.That(clip.samples, Is.EqualTo(14400));
                var samples = new float[28800];
                Assert.That(clip.GetData(samples, 0), Is.True);
                Assert.That(samples[0], Is.Zero); Assert.That(samples[28799], Is.Zero);
                for (int i = 0; i < 14400; i++) Assert.That(samples[2 * i], Is.EqualTo(samples[2 * i + 1]));
            }
            Assert.That(app.StartSession(), Is.True); Training(); Push("80");
            yield return null; yield return null;
            Assert.That(app.Output.AudioSource.clip, Is.EqualTo(app.Output.Clip(FeedbackState.Long)));
            Assert.That(app.Output.AudioSource.isPlaying, Is.True);
            Assert.That(app.View.Gauge.Visible, Is.False);
            app.EndSession();
        }

        [UnityTest]
        public IEnumerator SessionSettingsLockAndResetWithoutLeakingPriorPresentation()
        {
            app.View.Group.value = (int)StudyGroup.Visual;
            Assert.That(app.StartSession(), Is.True); Training(); Push("70");
            yield return null; yield return null;
            Assert.That(app.View.Group.interactable || app.View.Volume.interactable || app.View.BaselineFirst.interactable, Is.False);
            string previous = app.SessionDirectory;
            app.EndSession(); app.View.Group.value = (int)StudyGroup.Auditory;
            app.View.BaselineFirst.text = "97, 97"; app.View.BaselineSecond.text = "97, 97";
            Assert.That(app.StartSession(), Is.True);
            Assert.That(app.SessionDirectory, Is.Not.EqualTo(previous));
            Assert.That(app.Engine.Context.Phase, Is.EqualTo(StudyPhase.Preparation));
            Assert.That(app.Engine.HasOpenContact || app.Engine.HasActiveFeedback, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.False);
            Assert.That(app.View.TargetText.text, Does.Contain("目標 97°"));
            app.EndSession();
        }

        [UnityTest]
        public IEnumerator HiddenOperatorPanelDoesNotHideOrAlterParticipantGauge()
        {
            app.View.Group.value = (int)StudyGroup.Visual; app.StartSession(); Training(); Push("70");
            yield return null; yield return null;
            app.View.ToggleOperator();
            Assert.That(app.View.OperatorPanel.activeSelf, Is.False);
            Assert.That(app.View.Gauge.Visible, Is.True);
            Assert.That(app.View.Gauge.Marker, Is.EqualTo(FeedbackState.Target));
            app.View.ToggleOperator(); app.EndSession();
        }

        [UnityTest]
        public IEnumerator InsufficientBaselineAndSaveFailurePreventStarting()
        {
            app.View.BaselineFirst.text = "60"; app.View.BaselineSecond.text = "60, 60";
            Assert.That(app.StartSession(), Is.False);
            Assert.That(Directory.Exists(root), Is.False);
            app.View.BaselineFirst.text = "60, 60";
            Directory.CreateDirectory(root);
            string blocked = Path.Combine(root, "file"); File.WriteAllText(blocked, "preserve");
            app.SaveRootOverride = blocked;
            Assert.That(app.StartSession(), Is.False);
            Assert.That(app.Running, Is.False);
            Assert.That(app.View.StatusText.text, Does.Contain("開始できません"));
            Assert.That(app.Output.AudioSource.isPlaying || app.View.Gauge.Visible, Is.False);
            yield return null;
        }
    }
}
