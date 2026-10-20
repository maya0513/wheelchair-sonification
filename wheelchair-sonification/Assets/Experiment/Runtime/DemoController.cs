using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public sealed class DemoController : MonoBehaviour
    {
        [SerializeField] private bool externalPresentation;
        public bool PresentationReady { get; private set; } = true;
        public string PresentationMode { get; set; } = "desktop";
        public string DemoMode { get; private set; } = "detailed";
        public string XrRuntime { get; set; }
        private readonly Queue<string> recent = new Queue<string>();
        private MonotonicClock clock;
        private DemoInput input;
        private TargetResult target;
        private string message;
        private string lastClassification;
        public DemoView View { get; private set; }
        public UnityFeedbackOutput Output { get; private set; }
        public ExperimentEngine Engine { get; private set; }
        public string SessionDirectory { get; private set; }
        public string SaveRootOverride { get; set; }
        public string SaveRoot => SaveRootOverride ?? Path.Combine(Application.persistentDataPath, "DemoSessions");
        public bool Running => Engine != null && Engine.State == SessionState.Running;

        private void Awake()
        {
            var font = Resources.Load<Font>("DemoJapanese");
            if (font == null) throw new InvalidOperationException("同梱の日本語フォントが見つかりません。");
            clock = new MonotonicClock();
            input = new DemoInput(clock);
            input.Received += received => Engine?.Process(received);
            View = new DemoView(transform, font, () => StartSession(), EndSession, Contact, Release,
                UpdateTarget, ChangeContext);
            Output = gameObject.AddComponent<UnityFeedbackOutput>();
            Output.Initialize(View.Gauge);
            if (!externalPresentation) CreateDesktopCamera();
            else PresentationReady = false;
            ReadCommandLine();
            UpdateTarget();
            try
            {
                int incomplete = FileSessionLog.CountIncomplete(SaveRoot);
                message = incomplete == 0 ? "準備中。設定を確認し、セッションを開始してください。" :
                    $"未完了の記録が {incomplete} 件あります。既存の記録は保持して、新しいセッションを作成します。";
            }
            catch (Exception exception) { message = "保存先を確認できません: " + exception.Message; }
            View.PathText.text = "保存先: " + SaveRoot;
            Refresh();
        }

        private void CreateDesktopCamera()
        {
            var cameraObject = new GameObject("Demo background", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(239, 244, 248, 255);
            camera.cullingMask = 0;
            camera.orthographic = true;
        }

        public void SetPresentationReady(bool ready, string reason)
        {
            if (!ready) InterruptSession(reason);
            PresentationReady = ready;
            message = reason;
            Refresh();
        }

        public void InterruptSession(string reason)
        {
            Engine?.Interrupt(reason);
            Engine = null;
            Output.Stop();
            Output.SetGaugeVisible(false);
            View.ResetContext();
            message = "中断しました（未完了として保存）: " + reason;
            Refresh();
        }

        public void SwitchMode(string mode)
        {
            if (mode != "quick" && mode != "detailed") throw new ArgumentException(nameof(mode));
            if (Running) EndSession();
            DemoMode = mode;
        }

        public bool QuickPush(StudyGroup group, float volume, FeedbackState state)
        {
            if (!PresentationReady) return false;
            if (DemoMode != "quick") SwitchMode("quick");
            if (Running && (View.Group.value != (int)group || View.Volume.value != volume)) EndSession();
            if (!Running)
            {
                View.BaselineFirst.text = View.BaselineSecond.text = "60, 60";
                View.Group.value = (int)group;
                View.Volume.value = volume;
                if (!StartSession()) return false;
                View.Phase.value = (int)StudyPhase.Training;
                View.Task.value = (int)StudyTask.Straight;
                View.Motion.value = (int)MotionState.Straight;
                View.CentralArea.isOn = true;
            }
            View.Angle.text = (60 + (int)state * 10).ToString(CultureInfo.InvariantCulture);
            Contact(); Release();
            return Running;
        }

        private void ReadCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--demo-log-root") SaveRootOverride = args[i + 1];
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) View.ToggleOperator();
            Engine?.Tick();
            Refresh();
        }

        public void UpdateTarget()
        {
            if (Running) return;
            if (!TryAngles(View.BaselineFirst.text, out double[] first) || !TryAngles(View.BaselineSecond.text, out double[] second))
                target = TargetResult.Failure("基準角をカンマ区切りの数値で入力してください。");
            else target = FeedbackProtocol.CalculateTarget(first, second);
            View.TargetText.text = target.Success ? string.Format(CultureInfo.InvariantCulture,
                "基準 {0:0.###}° → 目標 {1:0.###}°  （範囲 {2:0.###}–{3:0.###}°）",
                target.BaselineDegrees, target.TargetDegrees, target.TargetDegrees - FeedbackProtocol.ToleranceDegrees,
                target.TargetDegrees + FeedbackProtocol.ToleranceDegrees) : target.Error;
        }

        public bool StartSession()
        {
            if (!PresentationReady) return false;
            if (Running) return false;
            UpdateTarget();
            if (!target.Success) { message = target.Error; Refresh(); return false; }
            Engine?.Dispose();
            Engine = null;
            FileSessionLog file = null;
            try
            {
                TryAngles(View.BaselineFirst.text, out double[] first);
                TryAngles(View.BaselineSecond.text, out double[] second);
                StudyGroup group = (StudyGroup)View.Group.value;
                file = new FileSessionLog(SaveRoot, new DemoSessionSettings
                {
                    clock_origin_utc = clock.OriginUtc, group = group.ToString(),
                    baseline_repeat_1 = first, baseline_repeat_2 = second,
                    baseline_median_degrees = target.BaselineDegrees, target_degrees = target.TargetDegrees,
                    increment_degrees = target.IncrementDegrees, digital_volume = View.Volume.value,
                    unity_version = Application.unityVersion, build_guid = Application.buildGUID,
                    operating_system = SystemInfo.operatingSystem,
                    presentation_mode = PresentationMode, demo_mode = DemoMode, xr_runtime = XrRuntime
                });
                SessionDirectory = file.DirectoryPath;
                recent.Clear();
                lastClassification = null;
                View.EventsText.text = "";
                View.ResetContext();
                Output.SetVolume(View.Volume.value);
                Engine = new ExperimentEngine(clock, Output, file, group, target.TargetDegrees);
                Engine.Recorded += OnRecord;
                View.PathText.text = "今回の記録: " + SessionDirectory;
                message = "記録中。時点・課題・動作を設定し、接触開始 → 離手を入力してください。";
                Refresh();
                return Running;
            }
            catch (Exception exception)
            {
                try { file?.Dispose(); } catch { }
                Output.Stop();
                Output.SetGaugeVisible(false);
                message = "開始できません。保存先・設定を確認してください: " + exception.Message;
                Refresh();
                return false;
            }
        }

        public void ChangeContext()
        {
            if (Running) input.ChangeContext(View.ReadContext());
            Refresh();
        }

        public void Contact()
        {
            if (Running) input.Contact();
            Refresh();
        }

        public void Release()
        {
            if (!Running) return;
            if (!double.TryParse(View.Angle.text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle))
            {
                message = "角度を数値で入力してください。不正値の検証には NaN も使えます。";
                Refresh();
                return;
            }
            input.Release(angle);
            Refresh();
        }

        public void EndSession()
        {
            if (!Running) return;
            Engine.EndSession();
            message = "記録を終了しました。次のセッションでは設定を変更できます。";
            Refresh();
        }

        private void OnRecord(ExperimentRecord record)
        {
            recent.Enqueue(string.Format(CultureInfo.InvariantCulture, "{0:0.000}s  {1}{2}", record.TimeSeconds,
                record.Event, string.IsNullOrEmpty(record.Reason) ? "" : " / " + record.Reason));
            while (recent.Count > 3) recent.Dequeue();
            View.EventsText.text = string.Join("\n", recent);
            if (record.Event == "stroke_classified") lastClassification = string.Format(CultureInfo.InvariantCulture,
                "{0:0.###}°  /  {1}", record.AngleDegrees, record.Feedback?.ToString().ToUpperInvariant());
            if (record.Event == "stroke_invalid") lastClassification = "無効 / " + record.Reason;
        }

        private void Refresh()
        {
            View.UpdateControls(Running, target.Success && PresentationReady, Running && Engine.HasOpenContact);
            bool fault = Engine != null && Engine.State == SessionState.Faulted;
            View.StatusText.text = fault ? "処理を停止しました。記録は未完了です: " + Engine.Fault : message;
            View.StatusText.color = fault ? new Color32(157, 52, 48, 255) : DemoUi.Ink;
            View.StrokeText.text = (Running && Engine.HasOpenContact ? "接触中  /  " : "離手中  /  ") +
                (lastClassification ?? "判定なし");
        }

        private static bool TryAngles(string text, out double[] angles)
        {
            string[] tokens = text.Split(new[] { ',', '、', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            angles = new double[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
                if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out angles[i])) return false;
            return true;
        }

        private void OnDestroy() => Engine?.Dispose();
        private void OnApplicationQuit() => Engine?.Dispose();
    }
}
