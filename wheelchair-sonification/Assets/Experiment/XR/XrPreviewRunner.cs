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
            xr.EnableEditorPreview(false);
            if (Interactive)
            {
                gameObject.AddComponent<EditorXrSimulator>().Initialize(xr.Rig.Camera.transform);
                xr.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>().enableMouseInput = false;
                xr.Link.EndpointOverride = "http://127.0.0.1:8765"; xr.Link.Begin();
                Debug.Log("Editor模擬：PCコンソールで操作。H頭／[左／]右／Space視点。");
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
            xr.ShowParticipantPreview("接続確認\n123456", true);
            ShotImage("01-pairing", "装着前・実施者が読む接続確認番号");
            xr.ShowParticipantPreview("準備中", true);
            ShotImage("02-waiting", "停止中・短い共通の待機表示");
            xr.Rig.Camera.fieldOfView = 70; xr.Rig.Camera.backgroundColor = new Color32(210, 210, 210, 255);
            xr.ShowParticipantPreview("そのままお待ちください", true);
            ShotImage("03-waiting-narrow", "待機表示・70度・明背景");
            xr.Rig.Camera.fieldOfView = 90; xr.Rig.Camera.backgroundColor = new Color32(50, 50, 50, 255);
            foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
            {
                xr.ShowParticipantPreview("", false);
                xr.App.QuickPush(StudyGroup.Visual, .1f, state); xr.App.Engine.Tick();
                ShotImage("04-visual-" + state.ToString().ToLowerInvariant(), "V群・" + state + "・90度");
                xr.Rig.Camera.fieldOfView = 70; xr.Rig.Camera.backgroundColor = new Color32(210, 210, 210, 255);
                ShotImage("05-visual-narrow-" + state.ToString().ToLowerInvariant(), "V群・" + state + "・70度");
                xr.App.EndSession();
                xr.Rig.Camera.fieldOfView = 90; xr.Rig.Camera.backgroundColor = new Color32(50, 50, 50, 255);
            }
            foreach (StudyGroup group in new[] { StudyGroup.Auditory, StudyGroup.NoFeedback })
            {
                xr.ShowParticipantPreview("", false); xr.App.QuickPush(group, .1f, FeedbackState.Target); xr.App.Engine.Tick();
                ShotImage("06-" + group.ToString().ToLowerInvariant(), group + "・画面には何も追加しない");
                xr.Rig.Camera.fieldOfView = 70; xr.Rig.Camera.backgroundColor = new Color32(210, 210, 210, 255);
                ShotImage("07-narrow-" + group.ToString().ToLowerInvariant(), group + "・70度・明背景");
                xr.App.EndSession(); xr.Rig.Camera.fieldOfView = 90; xr.Rig.Camera.backgroundColor = new Color32(50, 50, 50, 255);
            }
            xr.ShowParticipantPreview("", false); xr.Gauge.SetVisible(false);
            ShotImage("08-countdown", "開始準備・秒数や開始ボタンはPCだけに表示");
            ShotImage("09-figure-eight", "8の字・操作画面もゲージもなし");
            ShotImage("10-assessment", "全群の評価・成績や追加FBなし");
            ShotImage("11-tracking-loss-moving", "追跡喪失・停止未確認・診断画面を出さない");
            xr.ShowParticipantPreview("準備中", true);
            ShotImage("12-stopped-after-loss", "実施者が安全停止を確認した後");
            ShotImage("13-export-success", "回収成功の詳細はPCだけ・被験者は待機");
            ShotImage("14-export-failure", "保存・回収失敗の詳細はPCだけ");
            xr.ShowParticipantPreview("", false); xr.App.QuickPush(StudyGroup.Visual, .1f, FeedbackState.Target); xr.App.Engine.Tick();
            xr.Rig.Camera.transform.rotation = Quaternion.Euler(0, 30, 0);
            ShotImage("15-head-turn", "頭部を右30度・必要なゲージだけ追従");
            xr.Rig.Camera.transform.rotation = Quaternion.identity; xr.Recenter();
            ShotImage("16-recenter", "正面・操作用の追従ウィンドウなし");
            xr.App.EndSession();
            yield break;
        }

        private void CaptureError(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

        private void ShotImage(string name, string title, bool offscreen = false)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var label in xr.GetComponentsInChildren<TMP_Text>())
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
            if (xr.WaitingLabel.gameObject.activeInHierarchy) shot.crop_title = Crop(image, xr.WaitingLabel.rectTransform, name + "-title.png");
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
