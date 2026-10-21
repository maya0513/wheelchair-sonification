using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    [Serializable] public sealed class OperatorCommand
    {
        public string id, visitId, action, value;
        public int revision;
        public double number;
        public bool flag;
    }
    [Serializable] public sealed class OperatorResult { public string id, message; public bool ok; }
    [Serializable] public sealed class DeviceSummary { public string key; public bool valid; public float speed; }
    [Serializable] public sealed class OperatorSnapshot
    {
        public int schema = 1, revision, tracked, quizQuestion, quizAttempt;
        public string visitId, participantId, title, instruction, kind, status, group, sourceKind, primaryLabel,
            calibrationInstruction, recordCounts, lastReason, quizModality, error, exportStatus, majorSide;
        public bool running, canStart, canRetry, canSkip, calibrated, saveHealthy, quizAwaiting, presentationReady, busy, targetAvailable, questionnaireStored, comprehensionFailed;
        public double elapsed, remaining, countdownRemaining, trialElapsed, targetDegrees, baselineDegrees, incrementDegrees;
        public int baselineFirstCount, baselineSecondCount;
        public DeviceSummary[] devices;
        public string[] bindings;
    }
    [Serializable] public sealed class SavedVisit
    {
        public VisitState state;
        public double[] baselineFirst, baselineSecond;
        public string sourceKind, sourceCommit;
        public bool baselineFirstComplete, baselineSecondComplete;
    }

    [DefaultExecutionOrder(300)]
    public sealed class ResearchVisitHost : MonoBehaviour
    {
        public ResearchController Research { get; private set; }
        public VisitEngine Visit { get; private set; }
        public string RootOverride { get; set; }
        public string VisitRoot => RootOverride ?? Path.Combine(Application.persistentDataPath, "ResearchVisits");
        public bool Busy { get; private set; }
        public string ExportPath { get; private set; }
        public string ExportStatus { get; private set; }
        public string Error { get; private set; }
        public bool OperatorConnected { get; private set; }
        public bool PhysicalStopConfirmed { get; private set; } = true;
        private bool storageHealthy = true;
        public Func<string, IPoseSource> SourceFactory;
        public Action<StudyGroup, double> Present;
        public Action StopPresentation;
        public event Action Changed;
        private readonly MonotonicClock clock = new MonotonicClock();
        private double operatorDeadline = double.NegativeInfinity, lastSave;
        private int lastRevision = -1;
        private bool initialized, saving, shuttingDown;
        private SyntheticPoseSource synthetic;
        private Task<string> exportTask;
        private string replacementId;
        private bool questionnaireStored;
        private DateTime visitCreatedUtc, lastObservedUtc;

        public void Initialize(ResearchController research)
        {
            Research = research; Research.OperatorManaged = true;
            Research.TrialClosed += TrialClosed; initialized = true;
        }
        public void ObserveOperator(bool alive, double leaseSeconds = VisitProtocol.OperatorTimeout)
        {
            operatorDeadline = alive ? clock.Now + Math.Min(VisitProtocol.OperatorTimeout, Math.Max(0, leaseSeconds)) : double.NegativeInfinity;
            OperatorConnected = alive && clock.Now < operatorDeadline;
        }
        private void Update()
        {
            if (!initialized) return;
            OperatorConnected = clock.Now < operatorDeadline;
            if (synthetic != null)
                synthetic.Moving = Research.State == TrialRunState.Running && (Research.Phase == StudyPhase.Training || Research.TrialElapsed < 12);
            if (Visit != null)
            {
                Visit.State.targetReady = Research.TargetReady;
                Visit.State.equipmentReady = Research.Wizard.Calibration.Complete && Research.PresentationReady && Research.SaveHealthy;
                Visit.Tick();
                var utc = DateTime.UtcNow;
                if (utc < lastObservedUtc.AddSeconds(-2))
                { Research.Interrupt("elapsed_time_unverified"); Visit.Interrupt("elapsed_time_unverified"); }
                if (utc > lastObservedUtc) lastObservedUtc = utc;
                Visit.State.elapsed = Math.Max(Visit.State.elapsed, Math.Max(0, (utc - visitCreatedUtc).TotalSeconds));
                if (Research.State == TrialRunState.Finishing && Visit.State.status == "active") { Visit.State.status = "finishing"; Visit.State.revision++; }
                if (!OperatorConnected && (Research.Active || Visit.State.status == "quiz")) Disconnect();
                if (Visit.State.lastReason == "visit_time_limit" && Research.Active) Research.Interrupt("visit_time_limit");
                if (!Busy && (Visit.State.revision != lastRevision || clock.Now - lastSave >= 1 && Visit.State.status != "done" && Visit.State.status != "terminated")) TrySave();
            }
            if (exportTask != null && exportTask.IsCompleted)
            {
                Busy = false;
                if (exportTask.IsFaulted) Error = "回収用ZIPを作成できません。元記録は保持：" + exportTask.Exception.GetBaseException().Message;
                else if (exportTask.Result.StartsWith("USB|", StringComparison.Ordinal)) ExportStatus = "USB取り出し先：" + exportTask.Result.Substring(4);
                else { ExportPath = exportTask.Result; ExportStatus = "ZIP作成済み。PCへ転送・照合しています。"; }
                exportTask = null; Changed?.Invoke();
            }
        }
        public void Disconnect()
        {
            OperatorConnected = false; StopPresentation?.Invoke();
            if (Research.Active) Research.Interrupt("connection_lost");
            if (Visit != null && Visit.State.status != "interrupted" && Visit.State.status != "done" && Visit.State.status != "terminated") Visit.Interrupt("connection_lost");
            TrySave();
        }
        public OperatorResult Execute(OperatorCommand c)
        {
            var result = new OperatorResult { id = c?.id, ok = false };
            try
            {
                if (c == null || string.IsNullOrEmpty(c.id) || c.id.Length > 128) throw new InvalidOperationException("操作識別が不正です。");
                if (Visit?.State.appliedCommands.Contains(c.id) == true) { result.ok = true; result.message = "処理済みの操作です。"; return result; }
                if (!OperatorConnected && c.action != "abort") throw new InvalidOperationException("実施者の接続を確認してください。");
                if (Busy)
                {
                    if (c.action != "abort") throw new InvalidOperationException("記録の回収処理を待ってください。");
                    StopPresentation?.Invoke(); Research.Interrupt("operator_stop_during_export");
                    result.ok = true; result.message = "提示停止済み。回収処理中のファイルは保持します。"; return result;
                }
                if (c.action != "abort" && c.action != "createVisit" && c.action != "resumeVisit" &&
                    (c.visitId != (Visit?.State.visitId ?? "") || c.revision != (Visit?.State.revision ?? 0))) throw new InvalidOperationException("画面の状態が古いので、最新状態を確認してください。");
                Error = null;
                Apply(c);
                if (Visit != null)
                {
                    Visit.State.appliedCommands.Add(c.id);
                    Journal(c, "accepted"); Save();
                }
                result.ok = true; result.message = "操作を受け付けました。";
            }
            catch (Exception e)
            {
                Error = result.message = e.Message;
                if (e is IOException || e is UnauthorizedAccessException)
                {
                    storageHealthy = false; StopPresentation?.Invoke(); Research.Interrupt("save_failure");
                    if (Visit != null && Visit.State.status != "done" && Visit.State.status != "terminated") Visit.Interrupt("save_failure");
                }
            }
            Changed?.Invoke(); return result;
        }
        private void Apply(OperatorCommand c)
        {
            if (c.action == "abort")
            {
                var reason = string.IsNullOrEmpty(c.value) ? "operator_stop" : c.value;
                StopPresentation?.Invoke(); Research.Interrupt(reason);
                if (Visit != null && Visit.State.status != "interrupted") Visit.Interrupt(reason);
                return;
            }
            if (c.action == "createVisit") { NewVisit((int)c.number, int.Parse(c.value)); return; }
            if (c.action == "resumeVisit") { ResumeLatest(); return; }
            if (c.action == "confirmStopped") { Require(!Research.Active, "走行・準備中は中止してから停止を確認してください。"); Research.ConfirmStopped(); PhysicalStopConfirmed = true; return; }
            if (c.action == "export" || c.action == "exportUsb") { BeginExport(c.action == "exportUsb"); return; }
            if (c.action == "quick")
            {
                Require(!Research.Active && (Visit == null || Visit.Current.kind == "equipment" || Visit.Current.id == "consent"), "模擬確認は実施前の停止中に限ります。");
                var parts = c.value.Split(':'); Present?.Invoke(ParseGroup(parts[0]), parts[1] == "Short" ? -10 : parts[1] == "Long" ? 10 : 0); return;
            }
            if (c.action == "assignRole" || c.action == "calibrate" || c.action == "resetCalibration" || c.action == "setVolume" || c.action == "setMajor" || c.action == "setInput")
            {
                Require(!Research.Active && Research.MenuAllowed && (Visit == null || Visit.Current.kind == "equipment" || Visit.Current.id == "consent" || Visit.State.status == "interrupted"), "実施前または中断対応中だけ設定できます。");
                switch (c.action)
                {
                    case "assignRole": Require(c.number >= 0 && c.number < 5 && c.number == (int)c.number, "役割が不正です。"); Research.AssignRole((TrackerRole)(int)c.number, c.value); break;
                    case "calibrate": Research.CaptureCalibration(); if (!Research.Wizard.Calibration.Complete && !string.IsNullOrEmpty(Research.Status) && Research.Status != Research.Wizard.Instruction) Error = Research.Status; break;
                    case "resetCalibration": Research.ResetCalibration(); break;
                    case "setVolume": Require(double.IsFinite(c.number) && c.number >= 0 && c.number <= 1, "音量は0–1です。"); Research.Output.SetVolume((float)c.number); if (Visit != null) Visit.State.volume = (float)c.number; break;
                    case "setMajor": Require(c.value == "right" || c.value == "left", "主要側が不正です。");
                        if (Visit != null) Require(Visit.State.trials.All(t => !t.stepId.StartsWith("pre-")), "事前評価後は主要側を変更できません。");
                        Research.SetMajorRight(c.value == "right"); if (Visit != null) Visit.State.majorRight = c.value == "right"; break;
                    case "setInput": Require(Visit == null || Visit.State.trials.Count == 0, "走行後は入力方式を変更できません。新しい来訪を作成してください。"); SetInput(c.value); break;
                }
                if (Visit != null) Visit.State.revision++;
                return;
            }
            Require(Visit != null, "実施前設定で来訪を作成してください。");
            switch (c.action)
            {
                case "start":
                    StopPresentation?.Invoke();
                    if (Visit.Current.kind == "trial")
                    {
                        Require(Visit.State.status == "ready" && !Visit.State.recoveryRequired, "中断への対応が必要です。");
                        Research.SetPhase(Visit.Current.phase); Research.SetTask(Visit.Current.task); Research.SetRepetition(Visit.Current.repetition);
                        Research.RetryOfTrialId = Visit.State.retryOfTrialId ?? replacementId;
                        Visit.StartStep(); Visit.State.activeTrialId = Guid.NewGuid().ToString("N");
                        Save(); // Reserve the trial durably before opening a recording or countdown.
                        if (!Research.BeginTrial(Visit.State.volume, Visit.State.activeTrialId))
                        { Visit.Interrupt("setup_failure"); Save(); throw new InvalidOperationException(Research.Status); }
                        PhysicalStopConfirmed = false;
                    }
                    else Visit.StartStep();
                    break;
                case "finishTrial": Require(Research.FinishTrial(), Research.Status); break;
                case "assignGroup": Visit.Assign(ParseGroup(c.value)); Research.SetGroup(ParseGroup(c.value)); break;
                case "answer": var answer = c.value.Split(':'); Require(answer.Length == 2, "状態と修正方向を入力してください。"); Visit.Answer(answer[0], answer[1]); break;
                case "reviewStopReason": Require(!Research.Active && PhysicalStopConfirmed, "物理的に停止してから理由を照合してください。"); Visit.ReviewStopReason(c.value); break;
                case "recover": Require(Research.MenuAllowed && PhysicalStopConfirmed, "物理的な停止確認と機器の対応が必要です。"); Research.VerifyStorage(); Visit.Recover(); break;
                case "retry": replacementId = Visit.State.activeTrialId; Visit.Retry(); break;
                case "skip": Visit.Skip(); replacementId = null; break;
                case "verifyRest": Visit.VerifyRest(c.number); break;
                case "questionnaire":
                    Require(Visit.Current.kind == "questionnaire" && Visit.State.status == "ready" && !Visit.State.recoveryRequired, "質問紙の時点・停止状態を確認してください。");
                    var questionnaire = JsonUtility.FromJson<Questionnaire>(c.value);
                    Require(questionnaire?.values?.Length == 8 && questionnaire.present?.Length == 8, "6尺度と不快・疲労の回答欄が必要です。");
                    for (int i = 0; i < 8; i++) Require(!questionnaire.present[i] || (questionnaire.values[i] >= 0 && questionnaire.values[i] <= (i < 6 ? 100 : 10) && (i >= 6 || questionnaire.values[i] % 5 == 0)), "尺度の範囲・刻みを確認してください。");
                    StoreQuestionnaire(c.value); Visit.State.questionnaireRecorded = true;
                    Visit.StartStep(); break;
                case "skipQuestionnaire": Require(Visit.Current.kind == "questionnaire" && Visit.State.status == "ready" && !Visit.State.recoveryRequired, "質問紙の時点・停止状態を確認してください。");
                    StoreQuestionnaire("{\"missing\":true,\"reason\":\"time_limit\"}"); Visit.State.questionnaireRecorded = true; Visit.StartStep(); break;
                case "useSavedQuestionnaire":
                    Require(Visit.Current.kind == "questionnaire" && questionnaireStored, "保存済みの質問紙がありません。");
                    Visit.State.questionnaireRecorded = true; Visit.StartStep(); break;
                case "terminate":
                    string endReason = Visit.Current.kind == "target" && !Research.TargetReady ? "baseline_insufficient" : Visit.State.quiz.failed ? "comprehension_failed" : (Visit.State.lastReason == "operator_stop" && c.value != "operator_stop" ? c.value : Visit.State.lastReason ?? c.value);
                    Research.Interrupt(endReason); StopPresentation?.Invoke(); Visit.Terminate(endReason); break;
                default: throw new InvalidOperationException("未対応の操作です。");
            }
        }
        [Serializable] private sealed class Questionnaire { public float[] values; public bool[] present; public string notes; }
        public void SetInput(string value)
        {
            Require(value == "actual" || value == "synthetic", "実測または明示した合成入力を選んでください。");
            synthetic = value == "synthetic" ? new SyntheticPoseSource() : null;
            Research.SetSource(synthetic ?? SourceFactory?.Invoke(value) ?? throw new InvalidOperationException("実機入力を初期化できません。"));
            if (synthetic != null) Research.Wizard.UseSyntheticCalibration(SyntheticPoseSource.Calibration());
        }
        private void NewVisit(int number, int enrollment)
        {
            Require(Research.MenuAllowed && (Visit == null || Visit.State.status == "done" || Visit.State.status == "terminated"), "現在の来訪を終了・中止してから作成してください。");
            Require(number >= 1 && number <= 999 && enrollment >= 1 && enrollment <= 999, "IDと登録順は1–999で指定してください。");
            if (Directory.Exists(VisitRoot))
                foreach (var file in Directory.GetFiles(VisitRoot, "visit.json", SearchOption.AllDirectories))
                {
                    var prior = JsonUtility.FromJson<SavedVisit>(File.ReadAllText(file));
                    Require(prior?.state != null, "既存の来訪記録を読めません。上書きせず確認してください。");
                    Require(prior.state.participantNumber != number && prior.state.enrollmentNumber != enrollment,
                        "その研究用IDまたは登録順は使用済みです。未終了なら復元し、別の参加者には別の番号を使ってください。");
                }
            questionnaireStored = false;
            string id = Guid.NewGuid().ToString("N");
            Research.NewVisit(number, id);
            if (synthetic != null) Research.Wizard.UseSyntheticCalibration(SyntheticPoseSource.Calibration());
            Attach(new VisitState { visitId = id, participantNumber = number, enrollmentNumber = enrollment, createdUtc = DateTime.UtcNow.ToString("O"), majorRight = Research.MajorRight });
            Directory.CreateDirectory(Path.Combine(VisitRoot, id)); Save();
        }
        private void Attach(VisitState state)
        {
            Visit = new VisitEngine(clock, state);
            lastObservedUtc = DateTime.UtcNow;
            if (!DateTime.TryParse(state.createdUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out visitCreatedUtc))
            { visitCreatedUtc = lastObservedUtc; Visit.Interrupt("elapsed_time_unverified"); }
            else
            {
                visitCreatedUtc = visitCreatedUtc.ToUniversalTime();
                double wallElapsed = (lastObservedUtc - visitCreatedUtc).TotalSeconds;
                if (wallElapsed < state.elapsed - 2) Visit.Interrupt("elapsed_time_unverified");
                state.elapsed = Math.Max(state.elapsed, Math.Max(0, wallElapsed));
            }
            Visit.Cue += (group, error) => { if (Research.PresentationReady && Research.Stationary && OperatorConnected) Present?.Invoke(group, error); else Visit.Interrupt("presentation_or_stationary_not_ready"); };
        }
        private void TrialClosed(bool complete, string id, string reason)
        {
            if (Visit == null || Visit.Current.kind != "trial" || (Visit.State.status != "active" && Visit.State.status != "finishing")) return;
            if (complete) { PhysicalStopConfirmed = true; Visit.FinishTrial(id); replacementId = null; }
            else
            {
                PhysicalStopConfirmed = false;
                Visit.State.trials.Add(new VisitTrial { trialId = id, stepId = Visit.Current.id, completed = false, reason = reason, elapsed = Visit.State.stepElapsed });
                Visit.State.activeTrialId = id;
                Visit.Interrupt(reason.Contains("tracking") ? "tracking_lost" : reason.Contains("save") ? "save_failure" : reason);
            }
            TrySave(); Changed?.Invoke();
        }
        public void Suspend(string reason)
        {
            if (!initialized) return;
            StopPresentation?.Invoke(); Research.Interrupt(reason);
            if (Visit != null && Visit.State.status != "done" && Visit.State.status != "terminated" && Visit.State.status != "interrupted") Visit.Interrupt(reason);
            TrySave();
        }
        private void ResumeLatest()
        {
            Require(Research.MenuAllowed && (Visit == null || Visit.State.status == "done" || Visit.State.status == "terminated"), "現在の来訪を閉じてから復元してください。");
            Require(Directory.Exists(VisitRoot), "復元できる来訪がありません。");
            var files = Directory.GetFiles(VisitRoot, "visit.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc);
            foreach (var file in files)
            {
                var saved = JsonUtility.FromJson<SavedVisit>(File.ReadAllText(file));
                if (saved?.state == null || saved.state.status == "done" || saved.state.status == "terminated") continue;
                Research.NewVisit(saved.state.participantNumber, saved.state.visitId);
                Research.SetMajorRight(saved.state.majorRight); Research.RestoreBaselines(saved.baselineFirstComplete ? saved.baselineFirst : null, saved.baselineSecondComplete ? saved.baselineSecond : null);
                if (saved.state.group != "unallocated") Research.SetGroup(ParseGroup(saved.state.group));
                Research.InvalidateCalibration("application_restart_reassign_and_recalibrate");
                if (saved.sourceKind == "synthetic_pose_fixture") SetInput("synthetic");
                else if (Research.SourceKind != "vive_ultimate_tracker") SetInput("actual");
                questionnaireStored = File.Exists(Path.Combine(VisitRoot, saved.state.visitId, "questionnaire.json"));
                Attach(saved.state);
                if (Visit.State.lastReason != "elapsed_time_unverified") Visit.Interrupt("application_restart"); PhysicalStopConfirmed = false;
                // A completion marker means the recording closed, not that its task/video was accepted.
                // Never repeat an already closed recording automatically after a crash window.
                if (Visit.State.lastReason != "elapsed_time_unverified" && Directory.Exists(Research.SaveRoot))
                    foreach (string trialFolder in Directory.GetDirectories(Research.SaveRoot))
                    {
                        string metadata = Path.Combine(trialFolder, "session.json");
                        if (!File.Exists(metadata)) continue;
                        var trial = JsonUtility.FromJson<ResearchSessionSettings>(File.ReadAllText(metadata));
                        if (trial.visit_id == saved.state.visitId && trial.trial_id == saved.state.activeTrialId && File.Exists(Path.Combine(trialFolder, "completed.json")))
                            Visit.Interrupt("completed_record_requires_review");
                    }
                Save(); return;
            }
            throw new InvalidOperationException("未終了の来訪がありません。");
        }
        private void StoreQuestionnaire(string data)
        {
            string file = Path.Combine(VisitRoot, Visit.State.visitId, "questionnaire.json");
            if (File.Exists(file)) throw new InvalidOperationException("保存済みの回答を保持しています。内容を照合し、保存済み回答で進んでください。");
            using var writer = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write), new System.Text.UTF8Encoding(false));
            writer.Write(data); writer.Flush(); questionnaireStored = true;
        }
        private void Save()
        {
            if (Visit == null || saving) return;
            saving = true;
            try
            {
                string folder = Path.Combine(VisitRoot, Visit.State.visitId); FileStorage.EnsureReserve(folder);
                var identity = Resources.Load<TextAsset>("BuildIdentity");
                var saved = new SavedVisit { state = Visit.State, baselineFirst = Research.BaselineFirst, baselineSecond = Research.BaselineSecond,
                    baselineFirstComplete = Research.BaselineFirst != null, baselineSecondComplete = Research.BaselineSecond != null,
                    sourceKind = Research.SourceKind, sourceCommit = identity != null ? identity.text.Trim() : "development" };
                string final = Path.Combine(folder, "visit.json"), temporary = final + ".partial";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(saved, true));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(final)) File.Replace(temporary, final, null); else File.Move(temporary, final);
                storageHealthy = true; lastSave = clock.Now; lastRevision = Visit.State.revision;
            }
            finally { saving = false; }
        }
        private void TrySave()
        {
            try { Save(); }
            catch (Exception e) { storageHealthy = false; Error = "来訪の保存失敗。元記録を保持して停止：" + e.Message;
                StopPresentation?.Invoke(); Research.Interrupt("save_failure");
                if (Visit != null && Visit.State.status != "interrupted") Visit.Interrupt("save_failure"); }
        }
        private void Journal(OperatorCommand command, string result)
        {
            string folder = Path.Combine(VisitRoot, Visit.State.visitId); Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "operator-events.jsonl"), JsonUtility.ToJson(new OperatorJournal { utc = DateTime.UtcNow.ToString("O"), elapsed = Visit.State.elapsed, command = command, result = result }) + "\n");
        }
        [Serializable] private sealed class OperatorJournal { public string utc, result; public double elapsed; public OperatorCommand command; }
        private void BeginExport(bool usb)
        {
            Require(!Research.Active && !Busy, "停止中だけ記録を回収できます。");
            StopPresentation?.Invoke(); Save(); Busy = true; ExportPath = null; ExportStatus = "終了・中断済み記録をまとめています。";
            string sessions = Research.SaveRoot, visits = VisitRoot;
            string output = Path.Combine(Application.persistentDataPath, "ExportStaging");
            var destination = usb ? new AndroidDownloadExport() : null;
            exportTask = Task.Run(() => {
                string zip = SessionExporter.CreateResearchBundle(sessions, visits, output);
                if (destination == null) return zip;
                string result = destination.Write(zip, Path.GetFileName(zip)); File.Delete(zip); return "USB|" + result;
            });
        }
        public void MarkExportFailed(string reason) { Error = ExportStatus = reason; ExportPath = null; Busy = false; }
        public bool BeginTransfer() { if (Research.Active || Busy) return false; Busy = true; return true; }
        public void ExportTransferred() { if (ExportPath != null && File.Exists(ExportPath)) File.Delete(ExportPath); ExportPath = null; Busy = false; ExportStatus = "PCへ回収・チェックサム照合済み。ゴーグル内の元記録は保持。"; }
        public OperatorSnapshot Snapshot()
        {
            var s = Visit?.State; var step = Visit?.Current;
            var devices = Research.Frame.poses.Select(p => new DeviceSummary { key = p.key, valid = p.valid, speed = Research.DeviceSpeed(p.key) ?? 0 }).ToArray();
            bool ready = s != null && s.status == "ready" && !s.recoveryRequired && !Busy;
            bool canStart = ready && storageHealthy && Research.SaveHealthy && step.kind != "allocation" && step.kind != "questionnaire" && step.kind != "interval" && step.kind != "done" &&
                !(step.kind == "rest" && s.restReview) && !(step.kind == "quiz" && s.quiz.failed);
            if (step?.kind == "trial") canStart &= Research.PresentationReady && Research.Wizard.Calibration.Complete && Research.Stationary;
            if (step?.kind == "equipment") canStart &= s.equipmentReady;
            if (step?.kind == "target") canStart &= Research.TargetReady;
            if (Research.Active) canStart = Research.Stationary && Research.State != TrialRunState.Countdown && !Busy && (Research.Phase != StudyPhase.Training || Research.TrialElapsed >= 120);
            int incomplete = Directory.Exists(Research.SaveRoot) ? FileSessionLog.CountIncomplete(Research.SaveRoot) : 0;
            int count = Directory.Exists(Research.SaveRoot) ? Directory.GetDirectories(Research.SaveRoot).Length : 0;
            return new OperatorSnapshot { revision = s?.revision ?? 0, visitId = s?.visitId ?? "", participantId = s == null ? "" : "P" + s.participantNumber.ToString("000"),
                title = s?.status == "terminated" ? "来訪中止" : step?.title ?? "実施前設定で来訪を作成", instruction = Error ?? (s?.quiz.failed == true ? "理解確認は再確認でも未達です。割付へ進めず、記録・例外対応で来訪を閉じてください。" : s?.status == "interrupted" ? "口頭で安全停止。記録・例外対応で原因と再試行・欠測・中止を確認してください。" : step?.instruction ?? "装着前に機器と接続を確認してください。"),
                kind = step?.kind ?? "setup", status = s?.status ?? "setup", group = s?.group ?? "unallocated", sourceKind = Research.SourceKind,
                elapsed = s?.elapsed ?? 0, remaining = Research.Active && Research.Phase == StudyPhase.Training ? Math.Max(0, 120 - Research.TrialElapsed) : Visit?.Remaining ?? 0, running = Research.Active, canStart = canStart,
                canRetry = Visit?.CanRetry ?? false,
                canSkip = s?.status == "interrupted" && !s.recoveryRequired && !VisitProtocol.Safety(s.lastReason),
                questionnaireStored = questionnaireStored, comprehensionFailed = s?.quiz.failed ?? false,
                primaryLabel = step?.kind == "quiz" && s.status == "ready" && !s.comprehensionPassed ? (s.quiz.attempt > 0 ? "再説明後、" : "") + (Visit.QuizModality == StudyGroup.Auditory ? "音の理解確認を開始" : "表示の理解確認を開始") : Research.State == TrialRunState.Countdown ? "開始準備中" : Research.Active ? "終了・安全停止を確認" : step?.kind == "trial" ? "この試行を開始" : step?.kind == "rest" ? "20分休憩を開始／確認" : "確認して次へ",
                tracked = devices.Count(d => d.valid), calibrated = Research.Wizard.Calibration.Complete, saveHealthy = Research.SaveHealthy && storageHealthy,
                presentationReady = Research.PresentationReady, countdownRemaining = Research.CountdownRemaining, trialElapsed = Research.TrialElapsed,
                calibrationInstruction = Research.Wizard.Instruction, devices = devices, bindings = Research.Wizard.Calibration.roles.Select(r => r.key).ToArray(),
                quizAwaiting = s?.status == "quiz" && s.quiz.awaitingAnswer, quizModality = Visit?.QuizModality.ToString(), quizQuestion = (s?.quiz.question ?? 0) + 1,
                quizAttempt = (s?.quiz.attempt ?? 0) + 1, majorSide = Research.MajorRight ? "right" : "left", targetAvailable = Research.TargetReady, targetDegrees = Research.TargetReady ? Research.Target.TargetDegrees : 0,
                baselineDegrees = Research.TargetReady ? Research.Target.BaselineDegrees : 0, incrementDegrees = Research.TargetReady ? Research.Target.IncrementDegrees : 0,
                baselineFirstCount = Research.BaselineFirst?.Length ?? 0, baselineSecondCount = Research.BaselineSecond?.Length ?? 0,
                recordCounts = "試行 " + count + "件 ／ 未完了 " + incomplete + "件。正式な完了・時間は動画で確認。", lastReason = s?.lastReason, error = Error, exportStatus = ExportStatus, busy = Busy };
        }
        private static StudyGroup ParseGroup(string value) { Require(Enum.TryParse(value, out StudyGroup g) && Enum.IsDefined(typeof(StudyGroup), g), "群が不正です。"); return g; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void OnApplicationQuit() { shuttingDown = true; Suspend("application_quit"); }
        private void OnDestroy() { if (!shuttingDown) Suspend("application_closed"); if (Research != null) Research.TrialClosed -= TrialClosed; }
    }
}
