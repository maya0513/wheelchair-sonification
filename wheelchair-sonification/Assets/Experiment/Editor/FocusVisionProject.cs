using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using VIVE.OpenXR;
using VIVE.OpenXR.Passthrough;
using VIVE.OpenXR.Tracker;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Editor
{
    public static class FocusVisionProject
    {
        public const string ScenePath = "Assets/Scenes/FeedbackDemoXR.unity";
        private const string XrSettingsPath = "Assets/XR/XRGeneralSettings.asset";

        // Run once to create the committed settings and Scene. Builds validate rather than silently fix them.
        [MenuItem("Experiment/Configure Focus Vision")]
        public static void Configure()
        {
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.Refresh();
            var sdk = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VIVEFocus3Feature).Assembly);
            File.Copy(Path.Combine(sdk.resolvedPath, "LICENSE.txt"), "Assets/StreamingAssets/HTC-OpenXR-LICENSE.txt", true);
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget targets))
            {
                targets = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(targets, XrSettingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, targets, true);
            }
            if (!targets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                targets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = targets.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = false; // Runtime creates/enables actions before InitializeLoader.
            var manager = general.Manager;
            if (!manager.activeLoaders.Any(loader => loader is OpenXRLoader) &&
                !XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                throw new InvalidOperationException("AndroidのOpenXR Loaderを設定できません。");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new InvalidOperationException("OpenXR設定を作成できません。");
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
                feature.enabled = feature is VIVEFocus3Feature || feature is VIVEFocus3Profile || feature is VivePassthrough || feature is ViveXRTracker;
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            var pipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Settings/Mobile_RPAsset.asset");
            if (pipeline != null)
            {
                var serialized = new SerializedObject(pipeline);
                var hdr = serialized.FindProperty("m_SupportsHDR");
                if (hdr != null) hdr.boolValue = false;
                serialized.FindProperty("m_RenderScale").floatValue = 1;
                serialized.FindProperty("m_MSAA").intValue = 4;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(targets);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Focus Vision demo");
                var controller = root.AddComponent<DemoController>();
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("externalPresentation").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<FocusVisionDemo>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(DemoBuildTools.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("Focus Vision configuration is ready.");
        }

        public static void Validate()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (general == null || general.InitManagerOnStart || general.Manager == null ||
                !general.Manager.activeLoaders.Any(loader => loader is OpenXRLoader))
                throw new InvalidOperationException("AndroidのOpenXR Loaderと手動初期化の設定が必要です。");
            if (settings == null || settings.GetFeature<VIVEFocus3Feature>()?.enabled != true ||
                settings.GetFeature<VIVEFocus3Profile>()?.enabled != true || settings.GetFeature<VivePassthrough>()?.enabled != true ||
                settings.GetFeature<ViveXRTracker>()?.enabled != true)
                throw new InvalidOperationException("VIVE XR Support・Controller・Passthrough・Ultimate Trackerを有効にしてください。");
            if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevel36 ||
                !PlayerSettings.Android.forceInternetPermission || PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
                throw new InvalidOperationException("遠隔操作用にAndroid target SDK 36・Internet権限・ローカルHTTP設定が必要です。");
            if (Resources.Load<TMPro.TMP_FontAsset>("XrJapanese") == null)
                throw new InvalidOperationException("日本語SDFフォントを準備してください。");
            if (!File.Exists(ScenePath) || PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).SequenceEqual(new[] { GraphicsDeviceType.OpenGLES3 }))
                throw new InvalidOperationException("XR SceneとOpenGLES3専用設定が必要です。");
        }
    }
}
