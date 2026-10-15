using System;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public sealed class DemoView
    {
        public InputField BaselineFirst { get; }
        public InputField BaselineSecond { get; }
        public Dropdown Group { get; }
        public Dropdown Phase { get; }
        public Dropdown Task { get; }
        public Dropdown Motion { get; }
        public Toggle CentralArea { get; }
        public Toggle Tracking { get; }
        public InputField Angle { get; }
        public Slider Volume { get; }
        public Button StartButton { get; }
        public Button EndButton { get; }
        public Button ContactButton { get; }
        public Button ReleaseButton { get; }
        public Text TargetText { get; }
        public Text StatusText { get; }
        public Text EventsText { get; }
        public Text PathText { get; }
        public Text StrokeText { get; }
        public GaugeView Gauge { get; }
        public GameObject OperatorPanel { get; }

        public DemoView(Transform parent, Font font, Action start, Action end, Action contact, Action release,
            Action setupChanged, Action contextChanged)
        {
            var ui = new DemoUi(font);
            var preview = DemoUi.Canvas("Participant preview", parent, 0);
            Gauge = new GaugeView(ui, preview);
            var operatorCanvas = DemoUi.Canvas("Operator controls", parent, 1);
            OperatorPanel = operatorCanvas.gameObject;
            var canvas = DemoUi.Rect("Operator layout", operatorCanvas, 0, 0, 1440, 960);
            canvas.anchorMin = canvas.anchorMax = new Vector2(0.5f, 1);
            canvas.pivot = new Vector2(0.5f, 1);
            canvas.anchoredPosition = Vector2.zero;
            DemoUi.EventSystem(parent);

            ui.Text(canvas, "App title", "フィードバック装置", 32, 22, 600, 45, 30);
            ui.Text(canvas, "Demo label", "模擬入力  /  技術検証用", 34, 68, 640, 28, 18, DemoUi.Muted);
            ui.Button(canvas, "操作パネルを隠す  [F1]", 1148, 30, 260, ToggleOperator);

            var setup = ui.Card(canvas, "01  セッション設定", 32, 112, 672, 300);
            ui.Text(setup, "Baseline label", "事前の有効角（カンマ区切り・度）", 22, 52, 620, 28, 17, DemoUi.Muted);
            ui.Text(setup, "Repeat 1 label", "反復1", 22, 92, 70, 42);
            BaselineFirst = ui.Input(setup, "Baseline repeat 1", "60, 60", 102, 92, 245);
            ui.Text(setup, "Repeat 2 label", "反復2", 368, 92, 70, 42);
            BaselineSecond = ui.Input(setup, "Baseline repeat 2", "60, 60", 448, 92, 202);
            TargetText = ui.Text(setup, "Calculated target", "", 22, 146, 628, 38, 19, DemoUi.Accent);
            Group = ui.Dropdown(setup, "Group", new[] { "A：聴覚", "V：視覚", "N：追加FBなし" }, 22, 197, 230);
            ui.Text(setup, "Volume label", "音量設定", 276, 197, 110, 34, 17);
            Volume = ui.Volume(setup, 390, 197, 182);
            var volumeText = ui.Text(setup, "Volume value", "0.10", 587, 197, 65, 34, 17);
            Volume.onValueChanged.AddListener(value => volumeText.text = value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            ui.Text(setup, "Setup note", "目標・群・音量は開始時に固定します。", 22, 254, 620, 28, 16, DemoUi.Muted);

            var context = ui.Card(canvas, "02  課題・計測状態", 736, 112, 672, 300);
            ui.Text(context, "Phase label", "時点", 22, 60, 80, 42);
            Phase = ui.Dropdown(context, "Phase", new[] { "準備", "事前評価", "訓練", "直後評価", "休憩", "休憩後評価" }, 112, 60, 225);
            ui.Text(context, "Task label", "課題", 358, 60, 70, 42);
            Task = ui.Dropdown(context, "Task", new[] { "なし", "10 m直進", "8の字" }, 438, 60, 212);
            ui.Text(context, "Motion label", "動作", 22, 120, 80, 42);
            Motion = ui.Dropdown(context, "Motion", new[] { "直進", "停止", "制動", "旋回" }, 112, 120, 225);
            Motion.SetValueWithoutNotify((int)MotionState.Stopped);
            CentralArea = ui.Toggle(context, "中央2–8 m内", false, 22, 183, 280);
            Tracking = ui.Toggle(context, "追跡が有効", true, 358, 183, 280);
            ui.Text(context, "Context note", "状態は手動入力です。実機による計測ではありません。", 22, 242, 628, 45, 16, DemoUi.Muted);

            var stroke = ui.Card(canvas, "03  模擬プッシュ", 32, 432, 672, 296);
            ui.Text(stroke, "Angle label", "離手時の既知角度", 22, 61, 225, 42);
            Angle = ui.Input(stroke, "Simulated angle", "70", 258, 61, 180);
            ui.Text(stroke, "Angle unit", "度", 452, 61, 55, 42);
            ContactButton = ui.Button(stroke, "接触開始", 22, 119, 302, () => contact());
            ReleaseButton = ui.Button(stroke, "離手・角度確定", 348, 119, 302, () => release(), true);
            StrokeText = ui.Text(stroke, "Stroke status", "接触開始 → 離手の順で入力してください。", 22, 183, 628, 42, 18);
            ui.Text(stroke, "Stroke note", "追跡不良や領域離脱も、プッシュの途中で試せます。", 22, 242, 628, 32, 16, DemoUi.Muted);

            var records = ui.Card(canvas, "04  記録", 736, 432, 672, 296);
            StartButton = ui.Button(records, "セッション開始", 22, 59, 302, () => start(), true);
            EndButton = ui.Button(records, "記録を終了", 348, 59, 302, () => end());
            PathText = ui.Text(records, "Session path", "", 22, 115, 628, 65, 14, DemoUi.Muted, TextAnchor.UpperLeft);
            EventsText = ui.Text(records, "Recent events", "", 22, 190, 628, 91, 15, DemoUi.Ink, TextAnchor.UpperLeft);

            StatusText = ui.Text(canvas, "Application status", "", 32, 746, 1376, 48, 18);
            ui.Text(canvas, "Preview explanation", "参加者用の提示プレビュー  /  操作欄の表示切替は F1", 32, 806, 1376, 30,
                16, DemoUi.Muted, TextAnchor.MiddleCenter);

            BaselineFirst.onValueChanged.AddListener(_ => setupChanged());
            BaselineSecond.onValueChanged.AddListener(_ => setupChanged());
            foreach (var dropdown in new[] { Phase, Task, Motion }) dropdown.onValueChanged.AddListener(_ => contextChanged());
            CentralArea.onValueChanged.AddListener(_ => contextChanged());
            Tracking.onValueChanged.AddListener(_ => contextChanged());
        }

        public ExperimentContext ReadContext() => new ExperimentContext((StudyPhase)Phase.value,
            (StudyTask)Task.value, (MotionState)Motion.value, CentralArea.isOn, Tracking.isOn);

        public void ResetContext()
        {
            Phase.SetValueWithoutNotify((int)StudyPhase.Preparation);
            Task.SetValueWithoutNotify((int)StudyTask.None);
            Motion.SetValueWithoutNotify((int)MotionState.Stopped);
            CentralArea.SetIsOnWithoutNotify(false);
            Tracking.SetIsOnWithoutNotify(true);
        }

        public void UpdateControls(bool running, bool validTarget, bool contactOpen)
        {
            BaselineFirst.interactable = BaselineSecond.interactable = Group.interactable = Volume.interactable = !running;
            StartButton.interactable = !running && validTarget;
            EndButton.interactable = running;
            ContactButton.interactable = running && !contactOpen;
            ReleaseButton.interactable = running && contactOpen;
        }

        public void ToggleOperator() => OperatorPanel.SetActive(!OperatorPanel.activeSelf);
    }
}
