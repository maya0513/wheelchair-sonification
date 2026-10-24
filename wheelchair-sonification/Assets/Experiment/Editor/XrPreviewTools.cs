using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WheelchairSonification.Editor
{
    public static class XrPreviewTools
    {
        private const string CaptureKey = "Wheelchair.XrCapture";
        private const string InteractiveKey = "Wheelchair.XrInteractive";
        [InitializeOnLoadMethod]
        private static void Install() => EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode) StartRequestedPreview(); };
        public static void Capture()
        {
            SessionState.SetString(CaptureKey, Environment.GetEnvironmentVariable("WHEELCHAIR_UI_PREVIEW_OUTPUT") ?? "");
            OpenPlayMode();
        }
        [MenuItem("Experiment/Open interactive XR preview")]
        public static void OpenInteractive() { SessionState.SetBool(InteractiveKey, true); OpenPlayMode(); }
        private static void OpenPlayMode()
        {
            ParticipantUiAssets.Prepare();
            EditorSceneManager.OpenScene(FocusVisionProject.ScenePath);
            EditorApplication.isPlaying = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedPreview()
        {
            string output = SessionState.GetString(CaptureKey, "");
            bool interactive = SessionState.GetBool(InteractiveKey, false);
            if (string.IsNullOrEmpty(output) && !interactive) return;
            SessionState.SetString(CaptureKey, ""); SessionState.SetBool(InteractiveKey, false);
            var runner = new GameObject("Editor-only XR preview").AddComponent<XrPreviewRunner>();
            runner.OutputDirectory = output;
            runner.Interactive = interactive;
        }
        public static void Replay()
        {
            string source = Environment.GetEnvironmentVariable("WHEELCHAIR_REPLAY_SOURCE");
            string output = Environment.GetEnvironmentVariable("WHEELCHAIR_REPLAY_OUTPUT");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output)) throw new ArgumentException("再生元と出力先が必要です。");
            string[] sessions = File.Exists(Path.Combine(source, "session.json")) ? new[] { source } : Directory.GetDirectories(source);
            int count = 0;
            foreach (string session in sessions)
            {
                if (!File.Exists(Path.Combine(session, "session.json"))) continue;
                var report = Runtime.PoseReplay.Run(session, Path.Combine(output, Path.GetFileName(session)));
                if (!report.classifications_match) throw new InvalidOperationException("再解析と元記録の速度・提示値が一致しません：" + session);
                count++;
            }
            if (count == 0) throw new InvalidOperationException("再生する記録がありません。");
            Debug.Log("Pose replay passed: " + count + " trials / " + output);
        }
    }
}
