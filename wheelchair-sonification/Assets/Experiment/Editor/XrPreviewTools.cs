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
            var report = Runtime.PoseReplay.Run(source, output);
            if (!report.classifications_match) throw new InvalidOperationException("再解析と元記録の判定が一致しません。" );
            Debug.Log("Pose replay passed: " + output);
        }
    }
}
