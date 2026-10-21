using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;
using TMPro;
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
        public FocusVisionRig Rig { get; private set; }
        public FocusVisionPanel Panel { get; private set; }
        public XrGauge Gauge { get; private set; }
        public ResearchController Research { get; private set; }
        public DemoController App { get; private set; }
        public ResearchVisitHost VisitHost { get; private set; }
        public OperatorLink Link { get; private set; }
        public TMP_Text WaitingLabel { get; private set; }
        public bool TechnicianPreview { get; private set; }
        private GameObject waitingRoot;
        private UltimatePoseSource actualInput;
        public bool IsEditorPreview { get; private set; }
        public bool Ready => ready;
        private Transform menuAnchor;
        private XRManagerSettings manager;
        private VivePassthrough feature;
        private XrPassthroughHTC passthrough;
        private bool hasPassthrough, nativeStarted, paused, ready, attempted, wasTracking, busy;
        private bool focused = true;
        private string runtimeStatus = "OpenXRを初期化しています。", userStatus;
        private double nextAttempt;
        private Coroutine quickRoutine;

        private void Awake()
        {
            App = GetComponent<DemoController>();
            App.PresentationMode = "focus-vision-openxr";
            App.SetPresentationReady(false, runtimeStatus);
            // Keep the desktop input adapter for the shared demo engine, but never render its dense UI in XR.
            App.View.OperatorPanel.SetActive(false);
            App.View.ParticipantCanvas.gameObject.SetActive(false);
            Rig = new FocusVisionRig(transform);
            menuAnchor = new GameObject("Menu anchor").transform;
            menuAnchor.SetParent(transform, false);
            var ui = new XrUi();
            var canvas = DemoUi.Canvas("XR feedback canvas", transform, 0);
            float scale = 2 * Mathf.Tan(12 * Mathf.Deg2Rad) / 600;
            Rig.WorldCanvas(canvas, Rig.Camera.transform, new Vector3(0, -Mathf.Tan(10 * Mathf.Deg2Rad), 1), new Vector2(600, 200), scale);
            Gauge = new XrGauge(ui, canvas);
            App.Output.BindGauge(Gauge);
            Research = gameObject.AddComponent<ResearchController>();
            Research.Output = App.Output;
            actualInput = new UltimatePoseSource();
            Research.SetSource(new BorrowedPoseSource(actualInput));
            Panel = new FocusVisionPanel(this, ui, Rig, menuAnchor);
            Panel.SetVisible(false);
            var waiting = DemoUi.Canvas("Participant waiting", transform, 1);
            Rig.WorldCanvas(waiting, Rig.Camera.transform, new Vector3(0, 0, 1.4f), new Vector2(850, 230), .001f);
            waitingRoot = waiting.gameObject;
            DemoUi.Fill(waiting, XrUi.Paper).raycastTarget = false;
            WaitingLabel = ui.Text(waiting, "Waiting text", "準備中", 24, 24, 802, 182, 56, TextAlignmentOptions.Center);
            VisitHost = gameObject.AddComponent<ResearchVisitHost>();
            VisitHost.Initialize(Research);
            VisitHost.SourceFactory = _ => new BorrowedPoseSource(actualInput);
            VisitHost.Present = PresentError; VisitHost.StopPresentation = StopQuick;
            Link = gameObject.AddComponent<OperatorLink>(); Link.Host = VisitHost;
            Research.StateChanged += OnTrialStateChanged;
            Recenter();
        }

        private IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Link.Begin();
            manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null) { SetReady(false, "AndroidのXR設定がありません。"); yield break; }
            yield return manager.InitializeLoader();
            if (manager.activeLoader == null) { SetReady(false, "OpenXRを起動できません。本体の状態を確認してください。"); yield break; }
            manager.StartSubsystems(); nativeStarted = true;
            feature = OpenXRSettings.Instance.GetFeature<VivePassthrough>();
            App.XrRuntime = OpenXRRuntime.name + " / " + OpenXRRuntime.version;
            foreach (var subsystem in manager.activeLoader.GetLoadedSubsystem<XRInputSubsystem>() is XRInputSubsystem input ? new[] { input } : Array.Empty<XRInputSubsystem>())
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
#else
            if (!IsEditorPreview) SetReady(false, "Editorの模擬確認は unity-preview-open から起動します。");
            yield break;
#endif
        }

        private void Update()
        {
            if (TechnicianPreview && Rig.TogglePressed) TogglePanel();
            if (TechnicianPreview && Rig.RecenterPressed) Recenter();
            Rig.UpdatePointers(TechnicianPreview && Panel.Visible && !Research.Active);
            if (!TechnicianPreview)
            {
                Panel.SetVisible(false);
                bool moving = Research.Active || App.Running || VisitHost.Visit?.State.status == "quiz";
                waitingRoot.SetActive(!moving && VisitHost.PhysicalStopConfirmed);
                WaitingLabel.text = !Link.Paired ? "接続確認\n" + (Link.PairingCode ?? "PCで準備") : "準備中";
                if (Research.Active) waitingRoot.SetActive(false);
            }
            else waitingRoot.SetActive(false);
            if (Panel.Visible && Panel.Page == XrPage.Devices) Panel.RefreshDevices();
            if (nativeStarted && !paused && focused) UpdateRuntime();
            foreach (var button in Panel.Tabs) button.interactable = !busy && Research.MenuAllowed && quickRoutine == null;
            if (Research.State == TrialRunState.Countdown)
                Panel.UpdateNotice("計測開始まで\n" + Math.Ceiling(Research.CountdownRemaining) + "秒");
            Panel.SetStatus(userStatus ?? (!ready ? runtimeStatus : Panel.Page == XrPage.Quick ? "模擬入力。最初は座って確認してください。" : Research.Status));
        }

        public void Recenter()
        {
            var forward = Vector3.ProjectOnPlane(Rig.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            menuAnchor.position = Rig.Camera.transform.position + forward * XrUi.Distance;
            menuAnchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void Notify(string message) { userStatus = message; Panel.SetStatus(message); }

        public void TogglePanel()
        {
            if (!TechnicianPreview) return;
            if (busy) return;
            userStatus = null;
            if (Research.Active || Research.State == TrialRunState.WaitingForStop)
            { Research.RequestMenu(); OnTrialStateChanged(); return; }
            StopQuick();
            if (Panel.KeypadVisible) { Panel.CancelNumber(); return; }
            if (Panel.Visible) Panel.SetVisible(false);
            else { Panel.Show(Panel.Page, Panel.Step); Recenter(); }
        }

        public void RunQuick(StudyGroup group, float volume, FeedbackState state)
        {
            if (!ready || !Research.MenuAllowed || busy) { Notify("準備完了・停止を確認してください。"); return; }
            StopQuick();
            if (!App.QuickPush(group, volume, state)) return;
            Panel.SetVisible(false);
            quickRoutine = StartCoroutine(ReturnFromQuick());
        }
        private IEnumerator ReturnFromQuick()
        {
            yield return new WaitForSecondsRealtime(1);
            App.EndSession(); quickRoutine = null;
            if (TechnicianPreview) { Panel.Show(XrPage.Quick); Recenter(); }
        }
        public void StopQuick()
        {
            if (quickRoutine != null) { StopCoroutine(quickRoutine); quickRoutine = null; }
            if (App.Running) App.EndSession();
            if (!Research.Active) { App.Output.Stop(); Gauge.SetVisible(false); }
        }

        public void PresentError(StudyGroup group, double error)
        {
            if (!ready || Research.Active || busy) return;
            StopQuick();
            if (!App.QuickError(group, VisitHost.Visit?.State.volume ?? .1f, error)) return;
            Panel.SetVisible(false); waitingRoot.SetActive(false);
            quickRoutine = StartCoroutine(ReturnFromQuick());
        }

        public void BeginResearch(float volume)
        {
            StopQuick(); userStatus = null;
            if (Research.BeginTrial(volume)) Panel.ShowNotice("計測開始まで\n10秒");
        }

        private void OnTrialStateChanged()
        {
            userStatus = null;
            if (!TechnicianPreview) { Panel.SetVisible(false); return; }
            if (Research.State == TrialRunState.Running) Panel.SetVisible(false);
            else if (Research.State == TrialRunState.Countdown) Panel.ShowNotice("計測開始まで\n10秒");
            else if (Research.State == TrialRunState.WaitingForStop)
                Panel.ShowNotice("安全に停止してください。", () => { Research.ConfirmStopped(); Panel.Show(XrPage.Settings, 1); Recenter(); });
            else { Panel.Show(XrPage.Settings, 1); Recenter(); }
        }

        private void UpdateRuntime()
        {
            if (feature == null || !feature.enabled) { SetReady(false, "パススルー機能が無効です。"); return; }
            bool tracking = InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            if (!tracking) { SetReady(false, "HMD追跡を待っています。復帰後に操作して再開。"); wasTracking = false; return; }
            if (!wasTracking) Recenter();
            wasTracking = true;
            if (!feature.XrSessionCreated || feature.XrSessionEnding)
            {
                hasPassthrough = false; attempted = false;
                SetReady(false, "OpenXRセッションの準備を待っています。"); return;
            }
            if (!hasPassthrough && !attempted && Time.realtimeSinceStartupAsDouble >= nextAttempt)
            {
                if (!OpenXRRuntime.IsExtensionEnabled("XR_HTC_passthrough"))
                { attempted = true; SetReady(false, "パススルー拡張が使えません。設定を確認してください。"); return; }
                attempted = true;
                var result = PassthroughAPI.CreatePlanarPassthrough(out passthrough, LayerType.Underlay);
                hasPassthrough = result == XrResult.XR_SUCCESS;
                if (!hasPassthrough) { SetReady(false, "パススルー生成失敗：" + result); return; }
            }
            if (hasPassthrough)
            {
                // Hands are free during propulsion. Losing an idle controller must not stop a real trial.
                SetReady(true, "パススルー準備完了");
            }
        }

        private void SetReady(bool value, string message)
        {
            if (ready == value && runtimeStatus == message) return;
            bool lost = ready && !value;
            ready = value; runtimeStatus = message;
            App.SetPresentationReady(value, message);
            Research.PresentationReady = value;
            if (lost)
            {
                VisitHost.Suspend("xr_or_head_tracking_lost");
                Research.InvalidateCalibration("xr_or_head_tracking_lost");
                OnTrialStateChanged();
                StopQuick();
                if (TechnicianPreview && !Panel.Visible) { Panel.Show(Panel.Page, Panel.Step); Recenter(); }
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem _)
        { VisitHost.Suspend("calibration_invalid"); Research.InvalidateCalibration("tracking_origin_changed_recalibrate"); }

        public string RecordCounts()
        {
            string Count(string root)
            {
                int total = Directory.Exists(root) ? Directory.GetDirectories(root).Count(p => File.Exists(Path.Combine(p, "session.json"))) : 0;
                return total + "件 ／ 未完了 " + FileSessionLog.CountIncomplete(root) + "件";
            }
            try { return "実測 " + Count(Research.SaveRoot) + "\n模擬 " + Count(App.SaveRoot); }
            catch (Exception e) { return "記録一覧を開けません：" + e.Message; }
        }
        public void Export(bool research)
        {
            if (busy || Research.Active) return;
            StopQuick(); busy = true; Notify("記録を書き出しています…");
            string root = research ? Research.SaveRoot : App.SaveRoot;
            string staging = Path.Combine(Application.persistentDataPath, "ExportStaging");
            var destination = new AndroidDownloadExport();
            StartCoroutine(WaitForOperation(System.Threading.Tasks.Task.Run(() =>
            {
                string archive = SessionExporter.CreateArchive(root, staging);
                string result = destination.Write(archive, Path.GetFileName(archive));
                File.Delete(archive); return "書き出し完了。Download内のExportsへ。";
            }), true));
        }
        public void ReplayLatest()
        {
            if (busy || Research.Active) return;
            busy = true; Notify("直近の実測を再解析しています…");
            string root = Research.SaveRoot;
            string output = Path.Combine(Application.persistentDataPath, "ReplayReports");
            StartCoroutine(WaitForOperation(System.Threading.Tasks.Task.Run(() => PoseReplay.Latest(root, output))));
        }
        private IEnumerator WaitForOperation(Task<string> task, bool showDestination = false)
        {
            while (!task.IsCompleted) yield return null;
            busy = false;
            Notify(task.IsFaulted ? "失敗。元記録は保持：" + task.Exception.GetBaseException().Message : task.Result);
            Panel.Show(XrPage.Records, !task.IsFaulted && showDestination ? 1 : 0);
        }
        public void Quit() { if (busy) { Notify("書き出し完了までお待ちください。"); return; } Research.Interrupt("operator_quit"); StopQuick(); Application.Quit(); }

#if UNITY_EDITOR
        public void ShowParticipantPreview(string text, bool visible)
        {
            TechnicianPreview = false; Panel.SetVisible(false);
            waitingRoot.SetActive(visible); WaitingLabel.text = text;
        }
        public void EnableEditorPreview(bool technician = true)
        {
            IsEditorPreview = true; TechnicianPreview = technician;
            Panel.SetVisible(technician); waitingRoot.SetActive(!technician);
            Rig.Camera.stereoTargetEye = StereoTargetEyeMask.None;
            Rig.Camera.backgroundColor = new Color32(50, 50, 50, 255);
            SetReady(true, "Editor模擬確認（実機性能は未検証）");
        }
#endif
        private void ReleasePassthrough()
        {
            if (hasPassthrough && feature != null && feature.XrSessionCreated && !feature.XrSessionEnding)
                PassthroughAPI.DestroyPassthrough(passthrough);
            hasPassthrough = false; attempted = false;
        }
        private void OnApplicationPause(bool pause)
        {
            paused = pause;
            if (App == null) return;
            if (pause) { VisitHost.Suspend("application_pause"); SetReady(false, "アプリ中断。操作して新しく開始してください。"); ReleasePassthrough(); }
            else nextAttempt = Time.realtimeSinceStartupAsDouble + .2;
        }
        private void OnApplicationFocus(bool focus)
        {
#if UNITY_EDITOR
            if (IsEditorPreview) return;
#endif
            focused = focus;
            if (!focus && App != null) { VisitHost.Suspend("application_focus_lost"); SetReady(false, "フォーカス喪失。実施者が復旧を確認してください。"); }
        }
        private void OnDestroy()
        {
            if (Research != null) Research.Interrupt("xr_scene_closed");
            if (App != null) App.InterruptSession("xr_scene_closed");
            actualInput?.Dispose();
            ReleasePassthrough();
            if (nativeStarted && manager != null)
            {
                var input = manager.activeLoader?.GetLoadedSubsystem<XRInputSubsystem>();
                if (input != null) input.trackingOriginUpdated -= OnTrackingOriginUpdated;
                manager.StopSubsystems(); manager.DeinitializeLoader();
            }
            Rig?.Dispose();
        }
    }
}
