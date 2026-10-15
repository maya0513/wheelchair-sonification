using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Editor
{
    public static class DemoBuildTools
    {
        public const string ScenePath = "Assets/Scenes/FeedbackDemo.unity";

        [MenuItem("Experiment/Open feedback demo")]
        public static void OpenDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else CreateDemoScene();
        }

        public static void CreateDemoScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("FeedbackDemo", typeof(DemoController));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("デモSceneを保存できませんでした。");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Experiment/Build Linux feedback demo")]
        public static void BuildLinux()
        {
            if (!File.Exists(ScenePath)) CreateDemoScene();
            string repository = Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));
            string output = Path.Combine(repository, ".local", "builds", "feedback-demo", "FeedbackDemo.x86_64");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development | BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Linuxビルドに失敗しました: " + report.summary.result);
            string notices = Path.Combine(Path.GetDirectoryName(output), "ThirdPartyNotices");
            Directory.CreateDirectory(notices);
            File.Copy(Path.Combine(Application.dataPath, "Experiment", "Fonts", "OFL.txt"),
                Path.Combine(notices, "Noto-OFL.txt"), true);
            Debug.Log("Feedback demo built: " + output);
        }
    }
}
