using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public enum TrialRunState { Idle, Countdown, Running, Finishing, WaitingForStop }

    [DefaultExecutionOrder(200)]
    public sealed class ResearchController : MonoBehaviour
    {
        public CalibrationWizard Wizard { get; } = new CalibrationWizard();
        public PoseFrame Frame { get; private set; } = new PoseFrame();
        public string RootOverride { get; set; }
        public string SaveRoot => RootOverride ?? Path.Combine(Application.persistentDataPath, "ResearchSessions");
        public string SourceKind => source?.Kind ?? "not_connected";
        public int ParticipantNumber { get; private set; } = 1;
        public string ParticipantId => "P" + ParticipantNumber.ToString("000");
        public StudyGroup Group { get; private set; } = StudyGroup.NoFeedback;
        public bool GroupAssigned { get; private set; }
        public bool MajorRight { get; private set; } = true;
        public StudyPhase Phase { get; private set; } = StudyPhase.Baseline;
        public StudyTask Task { get; private set; } = StudyTask.Straight;
        public int Repetition { get; private set; } = 1;
        public TrialRunState State { get; private set; }
        public bool Active => State == TrialRunState.Running || State == TrialRunState.Countdown || State == TrialRunState.Finishing;
        public bool MenuAllowed => State == TrialRunState.Idle;
        public bool PresentationReady { get; set; }
        public float SpeedMps => estimator?.SpeedMps ?? 0;
        public bool Stationary => estimator != null && estimator.TrackingValid && SpeedMps < Wizard.Calibration.stoppedSpeedMps;
        public double CountdownRemaining => Math.Max(0, countdownUntil - clock.Now);
        public string Status { get; private set; } = "5台の役割と校正を確認してください。";
        public string SessionDirectory { get; private set; }
        public TargetResult Target { get; private set; }
        public bool TargetReady => Target.Success;
        public double TrialElapsed => State == TrialRunState.Running || State == TrialRunState.Finishing ? clock.Now - trialStarted : 0;
        public UnityFeedbackOutput Output { get; set; }
        public event Action StateChanged;
        public event Action<bool, string, string> TrialClosed;
        public string VisitId => visitId;
        public string TrialId { get; private set; }
        public bool SaveHealthy { get; private set; } = true;
        public bool OperatorManaged { get; set; }
        public string RetryOfTrialId { get; set; }
        public double[] BaselineFirst => baselineCompleted[0] ? baselines[0].ToArray() : null;
        public double[] BaselineSecond => baselineCompleted[1] ? baselines[1].ToArray() : null;
        private readonly MonotonicClock clock = new MonotonicClock();
        private readonly FrameDispatchClock dispatchClock = new FrameDispatchClock();
        private readonly List<double>[] baselines = { new List<double>(), new List<double>() };
        private readonly bool[] baselineCompleted = new bool[2];
        private readonly List<double> trialAngles = new List<double>();
        private readonly Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> deviceSpeeds = new Dictionary<string, float>();
        public float? DeviceSpeed(string key) => key != null && deviceSpeeds.TryGetValue(key, out float speed) ? speed : (float?)null;
        private string visitId = Guid.NewGuid().ToString("N");
        private IPoseSource source;
        private PushEstimator estimator;
        private TrackingCalibration estimatorCalibration;
        private ExperimentEngine engine;
        private ResearchSessionLog log;
        private double countdownUntil, trialStarted, lastFlush, lastFrameTime, movementStarted = double.NaN, stillSince = double.NaN;
        private float startPosition, maximumTravel;

        public void SetSource(IPoseSource value)
        {
            Interrupt("input_source_changed");
            source?.Dispose(); source = value;
            Wizard.Reset(); estimator = null;
            lastPositions.Clear(); deviceSpeeds.Clear();
            Status = "5台の役割と校正を確認してください。";
        }

        public void AssignNext(TrackerRole role)
        {
            if (!MenuAllowed || Frame.poses.Count == 0) { Status = "接続されたトラッカーがありません。"; return; }
            var binding = Wizard.Calibration.roles[(int)role];
            int current = Frame.poses.FindIndex(p => p.key == binding.key);
            var pose = Frame.poses[(current + 1) % Frame.poses.Count];
            Wizard.Reset();
            binding.key = pose.key; binding.runtimeDeviceId = pose.runtimeDeviceId;
            estimator = null;
            Status = "1台ずつ動かし、役割と動きが一致するか確認。";
        }

        public void CaptureCalibration()
        {
            if (!MenuAllowed) return;
            string error = Wizard.Capture(Frame);
            Status = error ?? Wizard.Instruction;
            if (Wizard.Calibration.Complete) estimator = null;
        }

        public void ResetCalibration()
        { if (!MenuAllowed) return; Wizard.Reset(); estimator = null; Status = Wizard.Instruction; }

        public void NewParticipant(int delta) => NewVisit(Mathf.Clamp(ParticipantNumber + delta, 1, 999), Guid.NewGuid().ToString("N"));
        public void NewVisit(int participantNumber, string id)
        {
            if (!MenuAllowed) throw new InvalidOperationException("停止中だけ来訪を作成できます。");
            ParticipantNumber = Mathf.Clamp(participantNumber, 1, 999);
            foreach (var values in baselines) values.Clear();
            baselineCompleted[0] = baselineCompleted[1] = false;
            GroupAssigned = false; Group = StudyGroup.NoFeedback; Target = default; MajorRight = true;
            Phase = StudyPhase.Baseline; Task = StudyTask.Straight; Repetition = 1;
            visitId = id; Wizard.BeginParticipant(); estimator = null; SaveHealthy = true;
            Status = "新しい来訪です。手首の装着・校正と共通習熟を行います。";
        }
        public void RestoreBaselines(double[] first, double[] second)
        {
            foreach (var values in baselines) values.Clear();
            baselines[0].AddRange(first ?? Array.Empty<double>()); baselines[1].AddRange(second ?? Array.Empty<double>());
            baselineCompleted[0] = first != null; baselineCompleted[1] = second != null;
            if (baselineCompleted.All(x => x)) Target = FeedbackProtocol.CalculateTarget(baselines[0], baselines[1]);
        }
        public void AssignRole(TrackerRole role, string key)
        {
            if (!MenuAllowed) throw new InvalidOperationException("走行中は役割を変更できません。");
            var pose = Frame.Find(key) ?? throw new InvalidOperationException("接続中の機器を選んでください。");
            Wizard.Assign(role, key, pose.runtimeDeviceId); estimator = null;
        }

        public void SetGroup(StudyGroup group)
        { if (MenuAllowed && TargetReady && !GroupAssigned) { Group = group; GroupAssigned = true; Status = "群と目標を固定しました。"; } }
        public void SetMajorRight(bool right)
        {
            if (!MenuAllowed || baselineCompleted.Any(x => x)) return;
            MajorRight = right; estimator = null;
        }
        public void SetPhase(StudyPhase phase) { if (MenuAllowed) Phase = phase; }
        public void SetTask(StudyTask task) { if (MenuAllowed) Task = task; }
        public void SetRepetition(int repetition) { if (MenuAllowed) Repetition = Mathf.Clamp(repetition, 1, 3); }

        public bool BeginTrial(float volume, string requestedTrialId = null)
        {
            if (!MenuAllowed || !PresentationReady) { Status = "XRの準備完了と停止を確認してください。"; return false; }
            string error = Wizard.Calibration.BindingError(Frame);
            if (!Wizard.Calibration.Complete || error != null || !Stationary)
            { Status = error ?? "校正完了・有効な追跡・停止が必要です。"; return false; }
            if (Phase != StudyPhase.Baseline && Phase != StudyPhase.Preparation && (!TargetReady || !GroupAssigned))
            { Status = "事前2反復で目標を算定し、割付群を設定してください。"; return false; }
            if (Phase == StudyPhase.Baseline && Task == StudyTask.Straight &&
                (Repetition > 2 || baselineCompleted[Repetition - 1]))
            { Status = "基準の同じ反復は上書きできません。次の反復か、新しい参加者を選択してください。"; return false; }
            if (Phase != StudyPhase.Training && Repetition > 2)
            { Status = "評価は2反復です。反復1または2を選んでください。"; return false; }
            try
            {
                Output.Stop(); Output.SetGaugeVisible(false); Output.SetVolume(volume);
                var identity = Resources.Load<TextAsset>("BuildIdentity");
                var settings = new ResearchSessionSettings
                {
                    participant_id = ParticipantId, visit_id = visitId, trial_id = TrialId = requestedTrialId ?? Guid.NewGuid().ToString("N"),
                    replaces_trial_id = RetryOfTrialId,
                    phase = Phase.ToString(), task = Task.ToString(), repetition = Repetition,
                    group = GroupAssigned ? Group.ToString() : "unallocated", major_side = MajorRight ? "right" : "left",
                    input_source = SourceKind, clock_origin_utc = clock.OriginUtc,
                    target_available = TargetReady, target_degrees = TargetReady ? Target.TargetDegrees : 0,
                    baseline_median_degrees = TargetReady ? Target.BaselineDegrees : 0,
                    increment_degrees = TargetReady ? Target.IncrementDegrees : 0,
                    baseline_repeat_1 = baselines[0].ToArray(), baseline_repeat_2 = baselines[1].ToArray(),
                    digital_volume = volume, unity_version = Application.unityVersion, application_version = Application.version, build_guid = Application.buildGUID,
                    source_commit = identity != null ? identity.text.Trim() : "development_source_not_embedded",
                    operating_system = SystemInfo.operatingSystem
                };
                FileStorage.EnsureReserve(SaveRoot);
                log = new ResearchSessionLog(SaveRoot, settings, Wizard.Calibration, clock.Now);
                SaveHealthy = true;
                SessionDirectory = log.DirectoryPath;
                countdownUntil = clock.Now + 10;
                trialAngles.Clear(); lastFlush = clock.Now; maximumTravel = 0; stillSince = double.NaN;
                // The first sample of a new recording starts a new observation interval.
                // Opening files while idle must not become an in-trial tracking gap.
                estimator = null;
                State = TrialRunState.Countdown;
                Status = "10秒後に計測開始。実施者が準備と安全を確認します。";
                StateChanged?.Invoke(); return true;
            }
            catch (Exception e) { Fault(e); return false; }
        }

        private void Update()
        {
            if (source == null) return;
            try
            {
                Frame = source.Read(clock.Now) ?? new PoseFrame { time = clock.Now };
                dispatchClock.Now = Frame.time;
                foreach (var p in Frame.poses)
                {
                    if (p.valid && clock.Now > lastFrameTime && lastFrameTime > 0 && lastPositions.TryGetValue(p.key, out var prior))
                        deviceSpeeds[p.key] = Vector3.Distance(p.position, prior) / (float)(clock.Now - lastFrameTime);
                    else deviceSpeeds.Remove(p.key);
                    if (p.valid) lastPositions[p.key] = p.position; else lastPositions.Remove(p.key);
                }
                foreach (var binding in Wizard.Calibration.roles)
                {
                    var pose = Frame.Find(binding.key);
                    if (pose != null && pose.runtimeDeviceId != binding.runtimeDeviceId && Wizard.Calibration.bodyReady)
                    { InvalidateCalibration("tracker_reconnected_reassign_and_recalibrate"); return; }
                }
                Wizard.Observe(Frame);
                if (Wizard.Calibration.Complete)
                {
                    if (estimator == null || estimatorCalibration != Wizard.Calibration)
                    {
                        estimatorCalibration = Wizard.Calibration;
                        estimator = new PushEstimator(estimatorCalibration, MajorRight);
                        estimator.Received += value => engine?.Process(value);
                    }
                    estimator.Observe(Frame, State == TrialRunState.Running ? Phase : StudyPhase.Preparation, Task);
                }
                if (Active)
                {
                    log.Poses(Frame, Wizard.Calibration);
                    log.Diagnostic(clock.Now, "unity_frame_interval_s", lastFrameTime == 0 ? null : clock.Now - lastFrameTime);
                    if (clock.Now - lastFlush >= FeedbackProtocol.FlushIntervalSeconds) { FileStorage.EnsureReserve(SaveRoot); log.Flush(); lastFlush = clock.Now; }
                    if (estimator != null && !estimator.TrackingValid)
                    { Interrupt("tracker_tracking_lost"); State = TrialRunState.WaitingForStop; StateChanged?.Invoke(); return; }
                }
                lastFrameTime = clock.Now;
                if (State == TrialRunState.Countdown && clock.Now >= countdownUntil)
                {
                    if (!Stationary || !PresentationReady) { Interrupt("preparation_or_tracking_lost"); return; }
                    trialStarted = clock.Now; movementStarted = double.NaN; startPosition = estimator.CoursePositionM;
                    engine = new ExperimentEngine(dispatchClock, Output, log, Group, TargetReady ? Target.TargetDegrees : (double?)null);
                    engine.Recorded += record =>
                    { if ((record.Event == "stroke_measured" || record.Event == "stroke_classified") && record.AngleDegrees.HasValue) trialAngles.Add(record.AngleDegrees.Value); };
                    log.Trial(clock.Now, "started");
                    estimator = null; // Reset contact state at the actual trial start.
                    State = TrialRunState.Running; Status = "計測中。開始・終了の正式な採点は外部動画です。";
                    StateChanged?.Invoke();
                }
                if (State == TrialRunState.Running)
                {
                    engine.Tick();
                    if (engine.State == SessionState.Faulted) { Fault(new IOException(engine.Fault)); return; }
                    if (estimator != null)
                    {
                        if (double.IsNaN(movementStarted) && estimator.SpeedMps >= Wizard.Calibration.stoppedSpeedMps)
                        { movementStarted = clock.Now; log.Trial(clock.Now, "motion_onset_estimate", "software_guard_only_not_video_start"); }
                        maximumTravel = Mathf.Max(maximumTravel, Mathf.Abs(estimator.CoursePositionM - startPosition));
                        if (Stationary) { if (double.IsNaN(stillSince)) stillSince = clock.Now; } else stillSince = double.NaN;
                        if (!OperatorManaged && Phase != StudyPhase.Training && Task == StudyTask.Straight && maximumTravel >= 9.5f &&
                            !double.IsNaN(stillSince) && clock.Now - stillSince >= .5) CompleteTrial("stopped_after_straight_course");
                    }
                    double limit = Phase == StudyPhase.Training ? 120 : Task == StudyTask.FigureEight ? 60 : 90;
                    if (State == TrialRunState.Running && clock.Now - (Phase == StudyPhase.Training ? trialStarted : double.IsNaN(movementStarted) ? clock.Now : movementStarted) >= limit)
                    {
                        if (Phase == StudyPhase.Training)
                        {
                            engine.Process(ExperimentInput.ChangeContext(dispatchClock.Now, ExperimentContext.Preparation));
                            Output.Stop(); Output.SetGaugeVisible(false); State = TrialRunState.Finishing;
                            log.Trial(clock.Now, "training_window_ended", "finish_safely_recording_continues");
                            Status = "2分終了。新しい走行・周回を始めず、安全停止を確認してください。";
                        }
                        else { Interrupt("timeout"); State = TrialRunState.WaitingForStop; }
                        StateChanged?.Invoke();
                    }
                }
                if (State == TrialRunState.WaitingForStop && Stationary)
                { State = TrialRunState.Idle; StateChanged?.Invoke(); }
            }
            catch (Exception e) { Fault(e); }
        }

        public void RequestMenu()
        {
            if (Active) Interrupt("operator_stop");
            StateChanged?.Invoke();
        }
        public bool FinishTrial()
        {
            if (Phase == StudyPhase.Training && TrialElapsed < 120)
            { Status = "2分未満の訓練は通常終了にできません。続行できなければ中止理由を記録してください。"; return false; }
            if ((State != TrialRunState.Running && State != TrialRunState.Finishing) || !Stationary)
            { Status = "走行開始後、安全な停止を確認して終了してください。"; return false; }
            CompleteTrial("operator_end_while_stopped_video_scoring_required"); return true;
        }

        public void VerifyStorage()
        {
            if (Active) throw new InvalidOperationException("停止後に保存を確認してください。");
            FileStorage.EnsureReserve(SaveRoot);
            string probe = Path.Combine(SaveRoot, ".write-probe-" + Guid.NewGuid().ToString("N"));
            try { using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.WriteByte(1); file.Flush(true); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            SaveHealthy = true;
        }

        public void ConfirmStopped()
        {
            if (State != TrialRunState.WaitingForStop) return;
            // An explicit physical stop confirmation permits menus, never a new trial with invalid tracking.
            State = TrialRunState.Idle;
            Status = "停止確認済み。次の開始前に追跡・校正を確認。";
            StateChanged?.Invoke();
        }

        private void CompleteTrial(string reason)
        {
            if (State != TrialRunState.Running && State != TrialRunState.Finishing) return;
            log.Trial(clock.Now, "operator_or_software_end", reason);
            engine.EndSession();
            if (engine.State == SessionState.Faulted) { Fault(new IOException(engine.Fault)); return; }
            engine = null; log = null;
            if (Phase == StudyPhase.Baseline && Task == StudyTask.Straight)
            {
                baselines[Repetition - 1].AddRange(trialAngles); baselineCompleted[Repetition - 1] = true;
                if (baselineCompleted.All(x => x)) Target = FeedbackProtocol.CalculateTarget(baselines[0], baselines[1]);
            }
            State = Stationary ? TrialRunState.Idle : TrialRunState.WaitingForStop;
            Status = TargetReady ? "記録終了。個人目標を固定しました。" : "記録終了。必要な反復と有効本数を確認してください。";
            StateChanged?.Invoke();
            TrialClosed?.Invoke(true, TrialId, reason);
        }

        public void Interrupt(string reason)
        {
            bool trialWasActive = Active || log != null || engine != null;
            var saved = log;
            try
            {
                if (saved != null) saved.Trial(clock.Now, "interrupted", reason);
                if (engine != null) engine.Interrupt(reason);
                else saved?.Flush();
            }
            finally
            {
                engine?.Dispose(); engine = null;
                saved?.Dispose(); log = null;
                Output?.Stop(); Output?.SetGaugeVisible(false);
                State = trialWasActive && !Stationary ? TrialRunState.WaitingForStop : TrialRunState.Idle;
                Status = "未完了として保存：" + reason;
            }
            if (trialWasActive) TrialClosed?.Invoke(false, TrialId, reason);
        }

        public void InvalidateCalibration(string reason)
        { Interrupt(reason); Wizard.Calibration.Invalidate(reason); estimator = null; StateChanged?.Invoke(); }

        private void Fault(Exception e)
        {
            SaveHealthy = false;
            try { Interrupt("save_or_processing_failure"); } catch { log?.Dispose(); log = null; engine = null; }
            Output?.Stop(); Output?.SetGaugeVisible(false);
            Status = "保存・処理に失敗。停止しました：" + e.Message;
            Debug.LogWarning(Status); StateChanged?.Invoke();
        }
        private void OnDestroy() { Interrupt("application_closed"); source?.Dispose(); }
        private void OnApplicationQuit() => Interrupt("application_quit");
    }
}
