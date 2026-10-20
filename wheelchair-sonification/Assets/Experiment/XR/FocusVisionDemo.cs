using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using VIVE.OpenXR;
using VIVE.OpenXR.Passthrough;
using LayerType = VIVE.OpenXR.CompositionLayer.LayerType;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(DemoController))]
    public sealed class FocusVisionDemo : MonoBehaviour
    {
        private DemoController app;
        private FocusVisionRig rig;
        private Transform menuAnchor;
        private RectTransform quick;
        private RectTransform bar;
        private Text status;
        private Text quickStatus;
        private Dropdown quickGroup;
        private Slider quickVolume;
        private Button[] quickPushes;
        private XRManagerSettings manager;
        private VivePassthrough feature;
        private XrPassthroughHTC passthrough;
        private bool hasPassthrough;
        private bool nativeStarted;
        private bool paused;
        private bool focused = true;
        private bool detailed;
        private bool ready;
        private bool attempted;
        private bool wasTracking;
        private string runtimeStatus = "OpenXRを初期化しています。";
        private double nextAttempt;
        public XrNumberPad NumberPad { get; private set; }

        private void Awake()
        {
            app = GetComponent<DemoController>();
            app.PresentationMode = "focus-vision-openxr";
            app.SetPresentationReady(false, runtimeStatus);
            rig = new FocusVisionRig(transform);
            menuAnchor = new GameObject("Menu anchor").transform;
            menuAnchor.SetParent(transform, false);
            rig.WorldCanvas((RectTransform)app.View.OperatorPanel.transform, menuAnchor, Vector3.zero,
                new Vector2(1440, 960), 0.001f);
            var ui = new DemoUi(Resources.Load<Font>("DemoJapanese"));
            NumberPad = new XrNumberPad(ui, app.View.OperatorPanel.transform.Find("Operator layout"));
            foreach (var field in new[] { app.View.BaselineFirst, app.View.BaselineSecond, app.View.Angle }) NumberPad.Bind(field);

            rig.WorldCanvas(app.View.ParticipantCanvas, rig.Camera.transform, new Vector3(0, -0.35f, 1),
                new Vector2(1440, 960), 0.0014f);
            var gauge = app.View.Gauge.Root;
            gauge.anchorMin = gauge.anchorMax = gauge.pivot = new Vector2(0.5f, 0.5f);
            gauge.anchoredPosition = Vector2.zero;

            quick = DemoUi.Canvas("Quick controls", transform, 2);
            rig.WorldCanvas(quick, menuAnchor, Vector3.zero, new Vector2(900, 620), 0.0012f);
            var card = ui.Card(quick, "Focus Vision  /  模擬入力デモ", 0, 0, 900, 620);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            ui.Text(card, "Instructions", "座った状態で確認してください。実際の走行や角度計測は行いません。\nポインターを合わせ、トリガーで選択します。", 24, 60, 852, 80, 21);
            quickGroup = ui.Dropdown(card, "Feedback mode", new[] { "A：聴覚", "V：視覚", "N：追加FBなし" }, 24, 158, 320);
            quickGroup.SetValueWithoutNotify((int)StudyGroup.Visual);
            ui.Text(card, "Volume label", "音量", 380, 158, 80, 40, 21);
            quickVolume = ui.Volume(card, 470, 160, 320);
            quickPushes = new Button[3];
            string[] labels = { "短い  60°", "目標範囲  70°", "長い  80°" };
            for (int i = 0; i < 3; i++)
            {
                var state = (FeedbackState)i;
                quickPushes[i] = ui.Button(card, labels[i], 24 + i * 288, 240, 276,
                    () => app.QuickPush((StudyGroup)quickGroup.value, quickVolume.value, state), true);
            }
            quickStatus = ui.Text(card, "Quick status", "", 24, 310, 852, 92, 20);
            ui.Text(card, "Target note", "基準60°・目標70°。各ボタンが1回の接触→離手を模擬します。\nA / Xで操作欄を表示・非表示、B / Yで正面へ戻します。", 24, 410, 852, 70, 20);
            ui.Button(card, "詳細設定", 24, 520, 270, () => ShowMode(true));
            ui.Button(card, "記録を終了", 312, 520, 270, () => app.EndSession());
            ui.Button(card, "アプリ終了", 600, 520, 270, Application.Quit);
            quickGroup.onValueChanged.AddListener(_ => { if (app.Running) app.EndSession(); });
            quickVolume.onValueChanged.AddListener(_ => { if (app.Running) app.EndSession(); });

            bar = DemoUi.Canvas("XR controls", transform, 3);
            rig.WorldCanvas(bar, rig.Camera.transform, new Vector3(0, 0.50f, 1), new Vector2(900, 150), 0.0009f);
            var barCard = ui.Card(bar, "", 0, 0, 900, 150);
            barCard.anchorMin = barCard.anchorMax = barCard.pivot = new Vector2(0.5f, 0.5f);
            barCard.anchoredPosition = Vector2.zero;
            status = ui.Text(barCard, "XR status", "", 20, 8, 860, 60, 20);
            ui.Button(barCard, "操作欄を切替", 20, 86, 270, TogglePanel);
            ui.Button(barCard, "正面へ配置", 310, 86, 270, Recenter);
            ui.Button(barCard, "再確認 / 簡単操作", 600, 86, 280, () => { attempted = false; ShowMode(false); });
            ShowMode(false);
            Recenter();
        }

        private IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null) { SetReady(false, "AndroidのXR設定がありません。"); yield break; }
            yield return manager.InitializeLoader();
            if (manager.activeLoader == null) { SetReady(false, "OpenXRを起動できません。本体の状態・ログを確認してください。"); yield break; }
            manager.StartSubsystems();
            nativeStarted = true;
            feature = OpenXRSettings.Instance.GetFeature<VivePassthrough>();
            app.XrRuntime = OpenXRRuntime.name + " / " + OpenXRRuntime.version;
#else
            SetReady(false, "Editorでは配置のみ確認できます。Focus Vision上でAPKを起動してください。");
            yield break;
#endif
        }

        public void ShowMode(bool showDetailed)
        {
            NumberPad.Cancel();
            app.SwitchMode(showDetailed ? "detailed" : "quick");
            detailed = showDetailed;
            app.View.OperatorPanel.SetActive(detailed);
            quick.gameObject.SetActive(!detailed);
            Recenter();
        }

        public void TogglePanel()
        {
            NumberPad.Cancel();
            var panel = detailed ? app.View.OperatorPanel : quick.gameObject;
            panel.SetActive(!panel.activeSelf);
        }

        public void Recenter()
        {
            var forward = Vector3.ProjectOnPlane(rig.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            menuAnchor.position = rig.Camera.transform.position + forward * 1.4f + Vector3.up * 0.12f;
            menuAnchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void Update()
        {
            foreach (var dropdown in new[] { quickGroup, app.View.Group, app.View.Phase, app.View.Task, app.View.Motion })
                FocusVisionRig.PrepareDropdown(dropdown);
            rig.UpdatePointers();
            if (rig.TogglePressed) TogglePanel();
            if (rig.RecenterPressed) Recenter();
            if (nativeStarted && !paused && focused) UpdateRuntime();
            foreach (var button in quickPushes) button.interactable = ready;
            status.text = runtimeStatus;
            quickStatus.text = ready ? app.View.StatusText.text : runtimeStatus;
        }

        private void UpdateRuntime()
        {
            if (feature == null || !feature.enabled) { SetReady(false, "パススルー機能が無効です。"); return; }
            bool tracking = InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            if (!tracking)
            {
                SetReady(false, "HMDの追跡を待っています。復帰後は操作して再開してください。");
                wasTracking = false;
                return;
            }
            if (!wasTracking) Recenter();
            wasTracking = true;
            if (!feature.XrSessionCreated || feature.XrSessionEnding)
            {
                hasPassthrough = false; // The SDK destroys handles with their OpenXR session.
                attempted = false;
                SetReady(false, "OpenXRセッションの準備を待っています。");
                return;
            }
            if (!hasPassthrough && !attempted && Time.realtimeSinceStartupAsDouble >= nextAttempt)
            {
                if (!OpenXRRuntime.IsExtensionEnabled("XR_HTC_passthrough"))
                { attempted = true; SetReady(false, "本体のパススルー拡張を使用できません。ROMとXR設定を確認してください。"); return; }
                attempted = true;
                var result = PassthroughAPI.CreatePlanarPassthrough(out passthrough, LayerType.Underlay);
                hasPassthrough = result == XrResult.XR_SUCCESS;
                if (!hasPassthrough) { SetReady(false, "パススルーの生成に失敗: " + result + "。再確認ボタンで再試行できます。"); return; }
            }
            if (hasPassthrough)
            {
                if (!rig.AnyControllerTracked) SetReady(false, "コントローラーの追跡を待っています。左右いずれかを使ってください。");
                else SetReady(true, "パススルー準備完了 / 模擬入力 / 座った状態で操作");
            }
        }

        private void SetReady(bool value, string message)
        {
            if (ready == value && runtimeStatus == message) return;
            ready = value;
            runtimeStatus = message;
            app.SetPresentationReady(value, message);
        }

        private void ReleasePassthrough()
        {
            if (hasPassthrough && feature != null && feature.XrSessionCreated && !feature.XrSessionEnding)
                PassthroughAPI.DestroyPassthrough(passthrough);
            hasPassthrough = false;
            attempted = false;
        }

        private void OnApplicationPause(bool pause)
        {
            paused = pause;
            if (app == null) return;
            if (pause) { SetReady(false, "アプリを一時中断しました。操作して新しく開始してください。"); ReleasePassthrough(); }
            else nextAttempt = Time.realtimeSinceStartupAsDouble + 0.2;
        }

        private void OnApplicationFocus(bool focus)
        {
            focused = focus;
            if (!focus && app != null) SetReady(false, "フォーカスを失いました。復帰後に操作して再開してください。");
        }

        private void OnDestroy()
        {
            if (app != null) app.InterruptSession("xr_scene_closed");
            ReleasePassthrough();
            if (nativeStarted && manager != null) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
            rig?.Dispose();
        }
    }
}
