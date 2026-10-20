#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Editor
{
    public sealed class XrPreviewRunner : MonoBehaviour
    {
        public string OutputDirectory;
        public bool Interactive;
        private FocusVisionDemo xr;
        private RenderTexture target;
        private readonly List<Shot> shots = new List<Shot>();
        private readonly List<string> errors = new List<string>();
        [Serializable] private sealed class Shot
        {
            public string name, file, title, crop_title, crop_button, crop_gauge;
            public float assumed_vertical_fov_degrees;
            public bool light_background, intentional_world_panel_offscreen;
            public int width = 1440, height = 1440;
        }
        [Serializable] private sealed class Manifest
        {
            public bool passed, physical_device_measured = false;
            public string rendering = "Unity XR UI, Editor, URP Mobile settings; FOV/background are assumptions";
            public string unity_version, source_commit;
            public Shot[] shots;
            public string[] errors;
        }
        private sealed class Fixture : IPoseSource
        {
            public string Kind => "synthetic_pose_fixture";
            public PoseFrame Read(double time)
            {
                var f = new PoseFrame { time = time };
                Vector3[] points = { new Vector3(-.3f, .3f, 0), new Vector3(.3f, .3f, 0), new Vector3(-.3f, 0, 0), new Vector3(.3f, 0, 0), Vector3.zero };
                for (int i = 0; i < 5; i++) f.poses.Add(new TrackerPose { key = "fixture-" + i,
                    product = "VIVE Ultimate Tracker " + i, runtimeDeviceId = 100 + i, position = points[i], valid = true, acquiredTime = time });
                return f;
            }
            public void Dispose() { }
        }
        private IEnumerator Start()
        {
            yield return null; yield return null;
            xr = FindAnyObjectByType<FocusVisionDemo>();
            if (xr == null) { Fail("XR Sceneがありません。"); yield break; }
            xr.EnableEditorPreview();
            if (Interactive)
            {
                gameObject.AddComponent<EditorXrSimulator>().Initialize(xr.Rig.Camera.transform);
                xr.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>().enableMouseInput = false;
                xr.Notify("Editor模擬：H頭／[左／]右／Space視点／M欄／R正面");
                if (Environment.GetEnvironmentVariable("WHEELCHAIR_INTERACTIVE_SMOKE") == "1")
                {
                    for (int i = 0; i < 12; i++) yield return null;
                    bool devices = UnityEngine.InputSystem.InputSystem.devices.Any(d => d.layout == "XRSimulatedHMD") &&
                        UnityEngine.InputSystem.InputSystem.devices.Count(d => d.layout == "XRSimulatedController") == 2;
                    Debug.Log("XR simulator smoke: " + devices);
                    EditorApplication.Exit(devices && xr.Ready ? 0 : 1);
                }
                yield break;
            }
            Directory.CreateDirectory(OutputDirectory);
            Application.logMessageReceived += CaptureError;
            AudioListener.volume = 0; // Screenshots are visual verification, not a physical sound test.
            xr.App.SaveRootOverride = Path.Combine(OutputDirectory, "fixture-records");
            xr.Research.RootOverride = Path.Combine(OutputDirectory, "fixture-research");
            xr.Research.SetSource(new Fixture());
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            QualitySettings.renderPipeline = mobile;
            xr.Rig.Camera.fieldOfView = 90;
            xr.Rig.Camera.aspect = 1;
            target = new RenderTexture(1440, 1440, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            yield return null; yield return null;
            for (int i = 0; i < 5; i++)
            {
                var binding = xr.Research.Wizard.Calibration.roles[i];
                binding.key = "fixture-" + i; binding.runtimeDeviceId = 100 + i;
            }
            IEnumerator work = CaptureAll();
            while (true)
            {
                bool more; object value = null;
                try { more = work.MoveNext(); if (more) value = work.Current; }
                catch (Exception e) { errors.Add(e.ToString()); break; }
                if (!more) break;
                yield return value;
            }
            xr.StopQuick();
            if (shots.Count < 18) errors.Add("撮影ケースが不足しています。");
            try { MakeContactSheet(); } catch (Exception e) { errors.Add(e.ToString()); }
            var manifest = new Manifest { passed = errors.Count == 0, unity_version = Application.unityVersion,
                source_commit = Environment.GetEnvironmentVariable("WHEELCHAIR_SOURCE_COMMIT") ?? "unknown", shots = shots.ToArray(), errors = errors.ToArray() };
            File.WriteAllText(Path.Combine(OutputDirectory, "manifest.json"), JsonUtility.ToJson(manifest, true));
            Application.logMessageReceived -= CaptureError;
            target.Release(); Destroy(target);
            Debug.Log("XR visual preview: " + (manifest.passed ? "PASSED" : "FAILED") + " / " + string.Join("; ", errors));
            EditorApplication.Exit(manifest.passed ? 0 : 1);
        }

        private IEnumerator CaptureAll()
        {
            xr.Panel.Show(XrPage.Quick); yield return null;
            ShotImage("01-quick", "かんたん確認・90°・暗背景");
            xr.Rig.Camera.fieldOfView = 70; xr.Rig.Camera.backgroundColor = new Color32(210, 210, 210, 255);
            ShotImage("02-quick-narrow", "かんたん確認・70°・明背景");
            xr.Rig.Camera.fieldOfView = 90; xr.Rig.Camera.backgroundColor = new Color32(50, 50, 50, 255);
            xr.Panel.Show(XrPage.Devices); yield return null;
            ShotImage("03-roles", "機器・手首と車体の役割");
            xr.Panel.Show(XrPage.Devices, 1); yield return null;
            ShotImage("04-wheel-roles", "機器・左右車輪の役割");
            xr.Panel.Show(XrPage.Devices, 2); yield return null;
            ShotImage("05-calibration", "校正・車体基準");
            xr.Research.CaptureCalibration(); xr.Panel.Show(XrPage.Devices, 2); yield return null;
            ShotImage("06-wheel-calibration", "校正・3次元の車輪回転軸");
            xr.Panel.Show(XrPage.Settings); yield return null;
            ShotImage("07-study-setup", "実験設定・研究用IDと群");
            xr.Panel.Show(XrPage.Settings, 1); yield return null;
            ShotImage("08-trial-setup", "実験設定・課題と時点");
            xr.Panel.OpenNumber("音量（0–1）", "0.10", _ => { }); yield return null;
            ShotImage("09-number-pad", "数値入力・独自キーパッド");
            xr.Panel.CancelNumber();
            foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
            {
                xr.App.QuickPush(StudyGroup.Visual, .1f, state); xr.Panel.SetVisible(false); xr.App.Engine.Tick();
                ShotImage("10-gauge-" + state.ToString().ToLowerInvariant(), "視覚・" + state);
                xr.App.EndSession();
            }
            xr.App.QuickPush(StudyGroup.NoFeedback, .1f, FeedbackState.Target); xr.Panel.SetVisible(false); xr.App.Engine.Tick();
            ShotImage("11-no-feedback", "追加FBなし・ゲージなし"); xr.App.EndSession();
            xr.App.QuickPush(StudyGroup.Auditory, .1f, FeedbackState.Target); xr.Panel.SetVisible(false); xr.App.Engine.Tick();
            ShotImage("12-auditory", "聴覚条件・視覚ゲージなし"); xr.App.EndSession();
            xr.Panel.ShowNotice("計測開始まで\n10秒"); yield return null;
            ShotImage("13-countdown", "開始前・コントローラーを置く準備時間");
            xr.Panel.ShowNotice("追跡を失いました。\n安全に停止してください。", () => { }); yield return null;
            ShotImage("14-tracking-lost", "異常時・共通の停止確認");
            xr.Panel.Show(XrPage.Records); xr.Notify("書き出し完了。Download内のExportsへ。"); yield return null;
            ShotImage("15-records-export", "記録・取り出し完了");
            xr.Panel.Show(XrPage.Records, 1); yield return null;
            ShotImage("15-export-location", "記録・USBで取り出すフォルダー");
            xr.Panel.Show(XrPage.Records); yield return null;
            xr.Notify("保存失敗。元の記録は保持しています。"); yield return null;
            ShotImage("16-export-error", "記録・取り出し失敗");
            xr.Panel.Show(XrPage.Quick); xr.Recenter(); xr.Rig.Camera.transform.rotation = Quaternion.Euler(0, 30, 0);
            yield return null;
            ShotImage("17-head-turn", "右へ30°向いた場合・画面は空間内に固定", true);
            xr.Recenter(); yield return null;
            ShotImage("18-recenter", "ボタンで正面へ再配置");
        }

        private void CaptureError(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

        private void ShotImage(string name, string title, bool offscreen = false)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var label in xr.Panel.Root.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                if (label.isTextOverflowing) errors.Add(name + ": 文字が領域から溢れています: " + label.name + " / " + label.text);
            }
            var camera = xr.Rig.Camera;
            var shot = new Shot { name = name, title = title, file = name + ".png", assumed_vertical_fov_degrees = camera.fieldOfView,
                light_background = camera.backgroundColor.r > .5f, intentional_world_panel_offscreen = offscreen };
            if (xr.Panel.Visible && !offscreen)
            {
                var corners = new Vector3[4]; xr.Panel.Root.GetWorldCorners(corners);
                foreach (var corner in corners)
                { var p = camera.WorldToViewportPoint(corner); if (p.z <= 0 || p.x < .02 || p.x > .98 || p.y < .02 || p.y > .98) errors.Add(name + ": パネルが正面の視野に収まりません。"); }
            }
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URPの画像撮影をサポートしていません。");
            RenderPipeline.SubmitRenderRequest(camera, request);
            var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1440, 1440, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1440, 1440), 0, 0); image.Apply(); RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(OutputDirectory, shot.file), image.EncodeToPNG());
            if (xr.Panel.Visible && !offscreen)
            {
                shot.crop_title = Crop(image, xr.Panel.Root.Find("Title") as RectTransform, name + "-title.png");
                var button = xr.Panel.Root.Find("Page body").GetComponentInChildren<Button>();
                if (button != null) shot.crop_button = Crop(image, (RectTransform)button.transform, name + "-button.png");
            }
            if (xr.Gauge.Visible) shot.crop_gauge = Crop(image, xr.Gauge.Root, name + "-gauge.png");
            Destroy(image); shots.Add(shot);
        }

        private string Crop(Texture2D source, RectTransform rect, string file)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(c => xr.Rig.Camera.WorldToViewportPoint(c)).ToArray();
            int x = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.x) * source.width) - 6, 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.y) * source.height) - 6, 0, source.height - 1);
            int w = Mathf.Min(source.width - x, Mathf.CeilToInt(points.Max(p => p.x) * source.width) + 6 - x);
            int h = Mathf.Min(source.height - y, Mathf.CeilToInt(points.Max(p => p.y) * source.height) + 6 - y);
            if (w <= 0 || h <= 0) throw new InvalidOperationException("切り出し領域が不正です。");
            var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
            crop.SetPixels(source.GetPixels(x, y, w, h)); crop.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, file), crop.EncodeToPNG()); Destroy(crop); return file;
        }

        private void MakeContactSheet()
        {
            const int size = 360, columns = 4;
            int rows = (shots.Count + columns - 1) / columns;
            var sheet = new Texture2D(columns * size, rows * size, TextureFormat.RGB24, false);
            var thumbnailTarget = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            for (int i = 0; i < shots.Count; i++)
            {
                var image = new Texture2D(2, 2); image.LoadImage(File.ReadAllBytes(Path.Combine(OutputDirectory, shots[i].file)));
                Graphics.Blit(image, thumbnailTarget);
                var previous = RenderTexture.active; RenderTexture.active = thumbnailTarget;
                var thumb = new Texture2D(size, size, TextureFormat.RGB24, false); thumb.ReadPixels(new Rect(0, 0, size, size), 0, 0); thumb.Apply();
                RenderTexture.active = previous;
                sheet.SetPixels(i % columns * size, (rows - 1 - i / columns) * size, size, size, thumb.GetPixels());
                Destroy(thumb); Destroy(image);
            }
            sheet.Apply(); File.WriteAllBytes(Path.Combine(OutputDirectory, "contact-sheet.png"), sheet.EncodeToPNG());
            RenderTexture.ReleaseTemporary(thumbnailTarget); Destroy(sheet);
        }
        private void Fail(string reason)
        { Debug.LogError(reason); EditorApplication.Exit(1); }
    }
}

#endif
