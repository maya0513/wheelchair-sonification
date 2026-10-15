#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    // Opt-in verification inside the built application. It never runs during ordinary use.
    public sealed class DemoSmokeCheck : MonoBehaviour
    {
        [Serializable]
        private sealed class Result
        {
            public bool passed;
            public bool physical_output_measured = false;
            public string audio_capture = "AudioSource.GetOutputData (software DSP output)";
            public string error;
            public int width;
            public int height;
            public double[] audio_frequencies_hz;
        }
        private string artifactRoot;
        private readonly List<string> errors = new List<string>();
        private readonly Result result = new Result();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "--demo-smoke-output") continue;
                var checker = new GameObject("Demo build verification").AddComponent<DemoSmokeCheck>();
                checker.artifactRoot = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(artifactRoot);
            Application.logMessageReceived += CaptureError;
            IEnumerator verification = Verify();
            while (true)
            {
                bool more;
                object current = null;
                try { more = verification.MoveNext(); if (more) current = verification.Current; }
                catch (Exception exception) { errors.Add(exception.ToString()); break; }
                if (!more) break;
                yield return current;
            }
            result.passed = errors.Count == 0;
            result.error = string.Join("\n", errors);
            result.width = Screen.width;
            result.height = Screen.height;
            File.WriteAllText(Path.Combine(artifactRoot, "smoke-result.json"), JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= CaptureError;
            Debug.Log("Demo build verification: " + (result.passed ? "PASSED" : "FAILED") + " / " + result.error);
            Application.Quit(result.passed ? 0 : 1);
        }

        private void CaptureError(string condition, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition);
        }

        private IEnumerator Verify()
        {
            yield return null; yield return null;
            var app = FindFirstObjectByType<DemoController>();
            Require(app != null, "DemoController missing");
            app.SaveRootOverride = Path.Combine(artifactRoot, "sessions");
            Require(!app.Running && !app.Output.AudioSource.isPlaying && !app.View.Gauge.Visible, "Initial state must be silent");
            var font = Resources.Load<Font>("DemoJapanese");
            foreach (var text in app.GetComponentsInChildren<Text>(true))
            {
                font.RequestCharactersInTexture(text.text, text.fontSize);
                foreach (char character in text.text)
                    if (!char.IsControl(character)) Require(font.HasCharacter(character), "Missing glyph: " + character);
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(artifactRoot, "operator.png"));
            yield return new WaitForEndOfFrame();

            app.View.Group.value = (int)StudyGroup.Visual;
            Require(app.StartSession(), "Visual session could not start");
            Training(app); Push(app, "70");
            yield return null; yield return null;
            Require(app.View.Gauge.Marker == FeedbackState.Target && !app.Output.AudioSource.isPlaying, "Visual feedback mismatch");
            ScreenCapture.CaptureScreenshot(Path.Combine(artifactRoot, "visual.png"));
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.36f);
            Require(app.View.Gauge.Marker == null && app.View.Gauge.Visible, "Marker did not clear");
            Push(app, "80");
            yield return null; yield return null;
            app.View.ToggleOperator();
            ScreenCapture.CaptureScreenshot(Path.Combine(artifactRoot, "participant.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            app.View.ToggleOperator(); app.EndSession();

            app.View.Group.value = (int)StudyGroup.Auditory;
            Require(app.StartSession(), "Auditory session could not start");
            Training(app);
            result.audio_frequencies_hz = new double[3];
            string[] angles = { "60", "70", "80" };
            int[] expected = { 440, 660, 880 };
            for (int tone = 0; tone < 3; tone++)
            {
                var samples = new float[2048];
                // The first call enables sample history; give the audio thread time to fill it.
                app.Output.AudioSource.GetOutputData(samples, 0);
                Push(app, angles[tone]);
                yield return null; yield return null;
                Require(app.Output.AudioSource.isPlaying && !app.View.Gauge.Visible, "Auditory feedback mismatch");
                double sum = 0;
                double deadline = Time.realtimeSinceStartupAsDouble + 0.22;
                do
                {
                    yield return null;
                    app.Output.AudioSource.GetOutputData(samples, 0);
                    sum = 0;
                    foreach (float sample in samples) sum += sample * sample;
                } while (Math.Sqrt(sum / samples.Length) <= 0.005 && Time.realtimeSinceStartupAsDouble < deadline);
                var crossings = new List<double>();
                for (int i = 0; i < samples.Length; i++)
                {
                    if (i > 0 && samples[i - 1] < 0 && samples[i] >= 0)
                        crossings.Add(i - 1 - samples[i - 1] / (samples[i] - samples[i - 1]));
                }
                Require(Math.Sqrt(sum / samples.Length) > 0.005, "No software audio signal");
                Require(crossings.Count >= 2, "Insufficient audio cycles");
                double frequency = (crossings.Count - 1) * AudioSettings.outputSampleRate /
                    (crossings[crossings.Count - 1] - crossings[0]);
                result.audio_frequencies_hz[tone] = frequency;
                Require(Math.Abs(frequency - expected[tone]) < 2, "Unexpected output frequency: " + frequency);
                using (var writer = new StreamWriter(Path.Combine(artifactRoot, "audio-" + expected[tone] + ".csv")))
                {
                    writer.WriteLine("sample,software_output_channel_0");
                    for (int i = 0; i < samples.Length; i++)
                        writer.WriteLine(i + "," + samples[i].ToString("R", CultureInfo.InvariantCulture));
                }
                yield return new WaitForSecondsRealtime(0.36f);
            }
            app.EndSession();
            app.View.Group.value = (int)StudyGroup.NoFeedback;
            Require(app.StartSession(), "No-feedback session could not start");
            Training(app); Push(app, "70");
            yield return null; yield return null;
            Require(!app.Output.AudioSource.isPlaying && !app.View.Gauge.Visible, "No-feedback group produced output");
            app.EndSession();
            yield return null;
            foreach (string name in new[] { "operator.png", "visual.png", "participant.png" })
                Require(File.Exists(Path.Combine(artifactRoot, name)), "Missing screenshot: " + name);
            Require(FileSessionLog.CountIncomplete(app.SaveRoot) == 0, "Completed sessions missing their markers");
        }

        private static void Training(DemoController app)
        {
            app.View.Phase.value = (int)StudyPhase.Training;
            app.View.Task.value = (int)StudyTask.Straight;
            app.View.Motion.value = (int)MotionState.Straight;
            app.View.CentralArea.isOn = true;
        }
        private static void Push(DemoController app, string angle)
        {
            app.View.Angle.text = angle;
            app.View.ContactButton.onClick.Invoke();
            app.View.ReleaseButton.onClick.Invoke();
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
