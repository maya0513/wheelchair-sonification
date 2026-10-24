using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WheelchairSonification.Editor
{
    public static class ReleaseBuildTools
    {
        public const string ApplicationId = "io.github.maya0513.wheelchairsonification";

        // scripts/unity-release.mtsから呼ぶ。署名パスワードをコマンド引数やログへ出さない。
        public static void BuildAndroid()
        {
            if (Application.unityVersion != Required("WHEELCHAIR_EXPECTED_UNITY_VERSION"))
                throw new InvalidOperationException("ProjectVersion.txtと同じUnity Editorを使用してください。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity HubでAndroid Build Support、SDK & NDK Tools、OpenJDKを追加してください。");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("-buildTarget Androidを指定して起動してください。");
            ParticipantUiAssets.Prepare();
            FocusVisionProject.Validate();
            string directory = Required("WHEELCHAIR_BUILD_DIRECTORY");
            string keystore = Required("UNITY_ANDROID_KEYSTORE");
            string alias = Required("UNITY_ANDROID_KEY_ALIAS");
            string storePassword = Required("UNITY_ANDROID_KEYSTORE_PASSWORD");
            string aliasPassword = Required("UNITY_ANDROID_KEY_ALIAS_PASSWORD");
            if (!File.Exists(keystore)) throw new FileNotFoundException("署名用キーストアがありません。");
            string[] scenes = { FocusVisionProject.ScenePath };
            if (scenes.Length == 0 || scenes.Any(scene => !File.Exists(scene)))
                throw new InvalidOperationException("ビルド対象のSceneがないか、ファイルがありません。");
            if (!File.Exists(Path.Combine(Application.dataPath, "StreamingAssets", "Noto-OFL.txt")))
                throw new InvalidOperationException("同梱フォントのライセンスがありません。");
            if (!File.Exists(Path.Combine(Application.dataPath, "StreamingAssets", "Liberation-OFL.txt")))
                throw new InvalidOperationException("TMP既定フォントのライセンスがありません。");
            if (!File.Exists(Path.Combine(Application.dataPath, "StreamingAssets", "HTC-OpenXR-LICENSE.txt")))
                throw new InvalidOperationException("HTC OpenXRのライセンス案内がありません。");
            string version = Environment.GetEnvironmentVariable("UNITY_BUILD_VERSION") ?? PlayerSettings.bundleVersion;
            if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("アプリのバージョンが空です。");
            int versionCode = PlayerSettings.Android.bundleVersionCode;
            string code = Environment.GetEnvironmentVariable("UNITY_ANDROID_VERSION_CODE");
            if (code != null && (!int.TryParse(code, out versionCode) || versionCode < 1 || versionCode > 2100000000))
                throw new InvalidOperationException("UNITY_ANDROID_VERSION_CODEは1〜2100000000の整数にしてください。");

            var target = NamedBuildTarget.Android;
            var oldBackend = PlayerSettings.GetScriptingBackend(target);
            var oldConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(target);
            var oldArchitecture = PlayerSettings.Android.targetArchitectures;
            var oldId = PlayerSettings.GetApplicationIdentifier(target);
            var oldVersion = PlayerSettings.bundleVersion;
            int oldCode = PlayerSettings.Android.bundleVersionCode;
            bool oldSplit = PlayerSettings.Android.useAPKExpansionFiles;
            bool oldSigning = PlayerSettings.Android.useCustomKeystore;
            string oldStore = PlayerSettings.Android.keystoreName, oldAlias = PlayerSettings.Android.keyaliasName;
            string oldStorePass = PlayerSettings.Android.keystorePass, oldAliasPass = PlayerSettings.Android.keyaliasPass;
            bool oldDevelopment = EditorUserBuildSettings.development, oldDebugging = EditorUserBuildSettings.allowDebugging;
            bool oldProfiler = EditorUserBuildSettings.connectProfiler, oldBundle = EditorUserBuildSettings.buildAppBundle;
            bool oldExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            string identityPath = "Assets/Experiment/Resources/BuildIdentity.txt";
            if (File.Exists(identityPath)) throw new InvalidOperationException("一時ビルド識別ファイルが残っています。");
            try
            {
                File.WriteAllText(identityPath, Required("WHEELCHAIR_SOURCE_COMMIT"));
                AssetDatabase.ImportAsset(identityPath, ImportAssetOptions.ForceSynchronousImport);
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetApplicationIdentifier(target, ApplicationId);
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.Android.useAPKExpansionFiles = false;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keystorePass = storePassword;
                PlayerSettings.Android.keyaliasPass = aliasPassword;
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                Directory.CreateDirectory(directory);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = Path.Combine(directory, "WheelchairSonification.apk"),
                    target = BuildTarget.Android, options = BuildOptions.StrictMode
                });
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0
                    || (report.summary.options & BuildOptions.Development) != 0)
                    throw new InvalidOperationException("Androidリリースビルドに失敗しました: " + report.summary.result);
                File.WriteAllText(Path.Combine(directory, "build-info.json"), JsonUtility.ToJson(new BuildInfo
                {
                    result = report.summary.result.ToString(), unityVersion = Application.unityVersion,
                    applicationId = PlayerSettings.GetApplicationIdentifier(target), version = version,
                    versionCode = versionCode, scenes = scenes
                }, true));
                Debug.Log("Android release built: " + directory);
            }
            finally
            {
                AssetDatabase.DeleteAsset(identityPath);
                PlayerSettings.SetScriptingBackend(target, oldBackend);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, oldConfiguration);
                PlayerSettings.Android.targetArchitectures = oldArchitecture;
                PlayerSettings.SetApplicationIdentifier(target, oldId);
                PlayerSettings.bundleVersion = oldVersion;
                PlayerSettings.Android.bundleVersionCode = oldCode;
                PlayerSettings.Android.useAPKExpansionFiles = oldSplit;
                PlayerSettings.Android.useCustomKeystore = oldSigning;
                PlayerSettings.Android.keystoreName = oldStore;
                PlayerSettings.Android.keyaliasName = oldAlias;
                PlayerSettings.Android.keystorePass = oldStorePass;
                PlayerSettings.Android.keyaliasPass = oldAliasPass;
                EditorUserBuildSettings.development = oldDevelopment;
                EditorUserBuildSettings.allowDebugging = oldDebugging;
                EditorUserBuildSettings.connectProfiler = oldProfiler;
                EditorUserBuildSettings.buildAppBundle = oldBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExport;
                AssetDatabase.SaveAssets();
            }
        }

        static string Required(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("環境変数がありません: " + name);
            return value;
        }

        [Serializable]
        sealed class BuildInfo
        {
            public string result, unityVersion, applicationId, version;
            public string target = "Android", architecture = "ARM64", scriptingBackend = "IL2CPP";
            public string application = "bilateral-wheel-speed-xr", inputMode = "simulated-and-vive-ultimate-tracker";
            public string headsetUi = "participant-only", operatorControl = "offline-pc-browser-http";
            public int operatorProtocol = 2, androidTargetSdk = 36;
            public bool visitAutomation = false, participantControlsRequired = false;
            public string measurementValidation = "pending_wheel_speed_accuracy_latency_and_field_validation";
            public string targetDevice = "VIVE Focus Vision", xrLoader = "OpenXR";
            public bool passthrough = true, deviceTested = false;
            public string viveOpenXrVersion = "2.5.1", unityOpenXrVersion = "1.18.0";
            public bool development = false, customSigning = true;
            public int versionCode;
            public string[] scenes;
        }
    }
}
