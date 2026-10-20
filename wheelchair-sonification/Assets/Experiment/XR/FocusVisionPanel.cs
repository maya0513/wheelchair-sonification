using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    public enum XrPage { Quick, Devices, Settings, Records }

    public sealed class FocusVisionPanel
    {
        private readonly FocusVisionDemo owner;
        private readonly XrUi ui;
        private readonly RectTransform body;
        private readonly TMP_Text title, status, hint;
        private readonly GameObject tabs;
        private readonly Button[] tabButtons = new Button[4];
        private Action<string> numericCommit;
        private Action numericCancel;
        private TMP_Text numberValue;
        private string numberBuffer;
        private string numericTitle;
        private readonly Dictionary<TrackerRole, TMP_Text> roleLabels = new Dictionary<TrackerRole, TMP_Text>();
        public RectTransform Root { get; }
        public XrPage Page { get; private set; }
        public int Step { get; private set; }
        public bool KeypadVisible => numericCommit != null;
        public string NumberBuffer => numberBuffer;
        public StudyGroup QuickGroup { get; private set; } = StudyGroup.Visual;
        public float Volume { get; private set; } = .10f;
        public bool Visible => Root.gameObject.activeSelf;
        public IReadOnlyList<Button> Tabs => tabButtons;
        private ResearchController Research => owner.Research;
        public FocusVisionPanel(FocusVisionDemo owner, XrUi ui, FocusVisionRig rig, Transform anchor)
        {
            this.owner = owner; this.ui = ui;
            Root = DemoUi.Canvas("XR main panel", owner.transform, 2);
            rig.WorldCanvas(Root, anchor, Vector3.zero, new Vector2(XrUi.Width, XrUi.Height), XrUi.Scale);
            DemoUi.Fill(Root, XrUi.Paper).raycastTarget = false;
            title = ui.Text(Root, "Title", "推進フィードバック", 36, 20, 1008, 80, 56);
            tabs = DemoUi.Rect("Page tabs", Root, 36, 116, 1008, 88).gameObject;
            string[] labels = { "かんたん確認", "機器・校正", "実験設定", "記録" };
            for (int i = 0; i < labels.Length; i++)
            {
                var page = (XrPage)i;
                tabButtons[i] = ui.Button(tabs.transform, "Tab " + page, labels[i], i * 254, 0, 246, () => Show(page));
                tabButtons[i].GetComponentInChildren<TMP_Text>().fontSize = 36;
            }
            body = DemoUi.Rect("Page body", Root, 36, 230, 1008, 460);
            status = ui.Text(Root, "Status", "", 36, 700, 1008, 76, 36);
            hint = ui.Text(Root, "Controller help", "A/X 操作欄　B/Y 正面へ", 36, 778, 1008, 56, 36);
            Show(XrPage.Quick);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);
        public void SetStatus(string message)
        {
            message = message?.Replace('\n', ' ').Replace('\r', ' ');
            status.text = message == null ? "" : message.Length > 26 ? message.Substring(0, 25) + "…" : message;
        }

        private void Clear()
        {
            roleLabels.Clear();
            foreach (Transform child in body) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
        }

        public void Show(XrPage page, int step = 0)
        {
            numericCommit = null; numericCancel = null;
            Page = page; Step = step;
            tabs.SetActive(true); status.gameObject.SetActive(true); hint.gameObject.SetActive(true); title.text = "推進フィードバック";
            SetVisible(true); Clear();
            for (int i = 0; i < 4; i++) XrUi.Selected(tabButtons[i], i == (int)page);
            switch (page)
            {
                case XrPage.Quick: Quick(); break;
                case XrPage.Devices: Devices(); break;
                case XrPage.Settings: Settings(); break;
                case XrPage.Records: Records(); break;
            }
        }

        private void Quick()
        {
            ui.Text(body, "Demo target", "基準60° → 目標70°（模擬）", 0, 0, 1008, 62);
            string[] groups = { "聴覚", "視覚", "FBなし" };
            for (int i = 0; i < 3; i++)
            {
                var group = (StudyGroup)i;
                ui.Button(body, "Quick group " + group, groups[i], i * 344, 82, 320,
                    () => { owner.StopQuick(); QuickGroup = group; Show(Page); }, QuickGroup == group);
            }
            string[] labels = { "短い 60°", "目標 70°", "長い 80°" };
            for (int i = 0; i < 3; i++)
            {
                var state = (FeedbackState)i;
                ui.Button(body, "Quick push " + state, labels[i], i * 344, 194, 320, () => owner.RunQuick(QuickGroup, Volume, state));
            }
            ui.Button(body, "Volume down", "音量 −", 0, 306, 250, () => { Volume = Mathf.Max(0, Volume - .05f); Show(Page); });
            ui.Button(body, "Volume value", "音量 " + Volume.ToString("0.00", CultureInfo.InvariantCulture), 274, 306, 460,
                () => OpenNumber("音量（0–1）", Volume.ToString("0.00", CultureInfo.InvariantCulture), text =>
                {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && value >= 0 && value <= 1)
                    { Volume = value; Show(Page); }
                    else owner.Notify("音量は0–1の数値で入力してください。");
                }));
            ui.Button(body, "Volume up", "音量 ＋", 758, 306, 250, () => { Volume = Mathf.Min(1, Volume + .05f); Show(Page); });
            ui.Text(body, "Quick note", "座って確認。提示中は操作画面を隠します。", 0, 400, 1008, 56, 36);
        }

        private void Devices()
        {
            if (Step == 2)
            {
                ui.Text(body, "Calibration instruction", Research.Wizard.Instruction, 0, 0, 1008, 140);
                ui.Button(body, "Capture calibration", Research.Wizard.ActionLabel, 0, 160, 1008,
                    () => { Research.CaptureCalibration(); Show(Page, 2); });
                ui.Button(body, "Reset calibration", "最初から", 0, 278, 480, () => { Research.ResetCalibration(); Show(Page, 2); });
                ui.Button(body, "Back to roles", "役割へ戻る", 528, 278, 480, () => Show(Page));
                ui.Text(body, "Calibration limitation", "初期の推定方式です。精度は動画で検証します。", 0, 396, 1008, 64, 36);
                return;
            }
            ui.Text(body, "Devices detected", "検出 " + Research.Frame.poses.Count + " / 5台　動かして識別", 0, 0, 1008, 58);
            TrackerRole[] roles = Step == 0 ? new[] { TrackerRole.LeftWrist, TrackerRole.RightWrist, TrackerRole.Chassis } :
                new[] { TrackerRole.LeftWheel, TrackerRole.RightWheel };
            string[] names = Step == 0 ? new[] { "左手首", "右手首", "車体" } : new[] { "左車輪", "右車輪" };
            for (int i = 0; i < roles.Length; i++)
            {
                var role = roles[i];
                ui.Text(body, "Role " + role, names[i], 0, 66 + i * 100, 220, 88);
                var button = ui.Button(body, "Assign " + role, "未割当・押して選択", 238, 66 + i * 100, 770, () => { Research.AssignNext(role); Show(Page, Step); });
                roleLabels[role] = button.GetComponentInChildren<TMP_Text>();
            }
            ui.Button(body, "Other roles", Step == 0 ? "車輪の割当へ" : "手首・車体へ", 0, 372, 480, () => Show(Page, Step == 0 ? 1 : 0));
            ui.Button(body, "Calibration page", "校正へ進む", 528, 372, 480, () => Show(Page, 2));
            RefreshDevices();
        }

        public void RefreshDevices()
        {
            foreach (var entry in roleLabels)
            {
                var pose = Research.Wizard.Calibration.Pose(Research.Frame, entry.Key);
                float? speed = pose != null ? Research.DeviceSpeed(pose.key) : null;
                entry.Value.text = pose == null ? "未割当・押して選択" :
                    "機器 " + ShortProduct(pose.product) + (pose.valid ? " 有効 " : " 無効 ") +
                    (speed.HasValue ? Mathf.Min(999, speed.Value * 100).ToString("0") + " cm/s" : "-- cm/s");
            }
        }

        private static string ShortProduct(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.StartsWith("VIVE Ultimate Tracker", StringComparison.Ordinal) ? name.Substring("VIVE Ultimate Tracker".Length).Trim() : name;
        }

        private void Settings()
        {
            if (Step == 0)
            {
                Choice("研究用ID", Research.ParticipantId, 0, () => Research.NewParticipant(-1), () => Research.NewParticipant(1));
                ui.Text(body, "Allocation heading", Research.TargetReady ? "割付群（設定後は固定）" : "群は事前2反復後に設定", 0, 104, 1008, 58);
                string[] labels = { "聴覚", "視覚", "FBなし" };
                for (int i = 0; i < 3; i++)
                {
                    var group = (StudyGroup)i;
                    var button = ui.Button(body, "Study group " + group, labels[i], i * 344, 174, 320,
                        () => { Research.SetGroup(group); Show(Page); }, Research.GroupAssigned && Research.Group == group);
                    button.interactable = Research.TargetReady && !Research.GroupAssigned;
                }
                ui.Button(body, "Dominant right", "主要側：右", 0, 282, 480, () => { Research.SetMajorRight(true); Show(Page); }, Research.MajorRight);
                ui.Button(body, "Dominant left", "主要側：左", 528, 282, 480, () => { Research.SetMajorRight(false); Show(Page); }, !Research.MajorRight);
                ui.Button(body, "Trial settings", "次へ：課題・時点", 0, 372, 1008, () => Show(Page, 1));
            }
            else
            {
                var phases = new[] { StudyPhase.Baseline, StudyPhase.Training, StudyPhase.ImmediateAssessment, StudyPhase.RetentionAssessment };
                string[] phaseNames = { "事前評価", "訓練", "直後評価", "休憩後評価" };
                int p = Array.IndexOf(phases, Research.Phase); if (p < 0) p = 0;
                int selected = p;
                Choice("時点", phaseNames[p], 0, () => Research.SetPhase(phases[(selected + 3) % 4]), () => Research.SetPhase(phases[(selected + 1) % 4]));
                Choice("課題", Research.Task == StudyTask.Straight ? "10 m直進" : "8の字", 110,
                    () => Research.SetTask(Research.Task == StudyTask.Straight ? StudyTask.FigureEight : StudyTask.Straight),
                    () => Research.SetTask(Research.Task == StudyTask.Straight ? StudyTask.FigureEight : StudyTask.Straight));
                Choice("反復", Research.Repetition.ToString(), 220, () => Research.SetRepetition(Research.Repetition - 1), () => Research.SetRepetition(Research.Repetition + 1));
                ui.Button(body, "Start real trial", "実測開始（10秒後）", 0, 340, 660, () => owner.BeginResearch(Volume));
                ui.Button(body, "Back to study setup", "戻る", 708, 340, 300, () => Show(Page));
            }
        }

        private void Choice(string label, string value, float y, Action less, Action more)
        {
            ui.Text(body, label, label, 0, y, 210, 88, 40);
            ui.Button(body, label + " minus", "−", 220, y, 100, () => { less(); Show(Page, Step); });
            if (label == "研究用ID")
                ui.Button(body, label + " value", value, 344, y, 540, () => OpenNumber("参加者番号（1–999）", Research.ParticipantNumber.ToString(), text =>
                {
                    if (int.TryParse(text, out int number) && number >= 1 && number <= 999) { Research.NewParticipant(number - Research.ParticipantNumber); Show(Page); }
                    else owner.Notify("参加者番号は1–999です。");
                }));
            else ui.Text(body, label + " value", value, 344, y, 540, 88, 44, TextAlignmentOptions.Center);
            ui.Button(body, label + " plus", "＋", 908, y, 100, () => { more(); Show(Page, Step); });
        }

        private void Records()
        {
            if (Step == 1)
            {
                ui.Text(body, "Export destination heading", "Windowsで開くフォルダー", 0, 0, 1008, 70);
                ui.Text(body, "Export destination", "Download/\nWheelchairSonificationExports/", 0, 86, 1008, 136);
                ui.Text(body, "Export instructions", "USBのファイル転送でZIPをコピー。\n元の記録はアプリ内に残ります。", 0, 226, 1008, 128, 36);
                ui.Button(body, "Back to records", "記録へ戻る", 0, 364, 1008, () => Show(Page));
                return;
            }
            ui.Text(body, "Record counts", owner.RecordCounts(), 0, 0, 1008, 128, 40);
            ui.Button(body, "Export research", "実測記録を書き出す", 0, 132, 1008, () => owner.Export(true));
            ui.Button(body, "Export demo", "模擬記録を書き出す", 0, 244, 1008, () => owner.Export(false));
            ui.Button(body, "Replay latest", "直近の実測を再解析", 0, 356, 500, owner.ReplayLatest);
            ui.Button(body, "Export location", "取り出し先", 520, 356, 280, () => Show(Page, 1));
            ui.Button(body, "Quit", "終了", 820, 356, 188, owner.Quit);
        }

        public void ShowNotice(string message, Action confirm = null)
        {
            Clear(); tabs.SetActive(false); hint.gameObject.SetActive(false); SetVisible(true);
            title.text = "状態確認";
            ui.Text(body, "Notice", message, 0, 10, 1008, 230, 56, TextAlignmentOptions.Center);
            if (confirm != null) ui.Button(body, "Confirm stopped", "安全に停止しました", 0, 300, 1008, confirm);
        }

        public void UpdateNotice(string message)
        {
            var text = body.Find("Notice");
            if (text != null) text.GetComponent<TMP_Text>().text = message;
        }

        public void OpenNumber(string label, string initial, Action<string> commit)
        {
            var previousPage = Page; int previousStep = Step;
            numericCommit = commit; numericTitle = label;
            numericCancel = () => Show(previousPage, previousStep);
            numberBuffer = initial;
            BuildNumberPad();
        }

        private void BuildNumberPad()
        {
            Clear(); tabs.SetActive(false); title.text = numericTitle; SetVisible(true);
            numberValue = ui.Text(body, "Numeric value", numberBuffer, 0, -112, 1008, 88, 56);
            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "−" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                ui.Button(body, "Key " + key, key, i % 3 * 344, i / 3 * 100, 320, () => PressNumber(key));
            }
            ui.Button(body, "Numeric cancel", "取消", 0, 420, 320, CancelNumber);
            ui.Button(body, "Numeric delete", "1字削除", 344, 420, 320, () => PressNumber("backspace"));
            ui.Button(body, "Numeric commit", "確定", 688, 420, 320, CommitNumber);
            status.gameObject.SetActive(false); hint.gameObject.SetActive(false);
        }
        public void PressNumber(string key)
        {
            if (numericCommit == null) return;
            if (key == "backspace") numberBuffer = numberBuffer.Length > 0 ? numberBuffer.Substring(0, numberBuffer.Length - 1) : "";
            else if (key == "−") numberBuffer = numberBuffer.StartsWith("-") ? numberBuffer.Substring(1) : "-" + numberBuffer;
            else if (numberBuffer.Length < 24) numberBuffer += key;
            numberValue.text = numberBuffer;
        }
        public void CommitNumber()
        {
            if (numericCommit == null) return;
            var commit = numericCommit; var close = numericCancel; string value = numberBuffer;
            numericCommit = null; numericCancel = null; status.gameObject.SetActive(true); close(); commit(value);
        }
        public void CancelNumber()
        { var close = numericCancel; numericCommit = null; numericCancel = null; status.gameObject.SetActive(true); close?.Invoke(); }
    }
}
