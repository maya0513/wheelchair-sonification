using System;
using System.IO;
using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    public sealed class ResearchController : MonoBehaviour
    {
        public UnityFeedbackOutput Output;
        public string RootOverride;
        public string SaveRoot => RootOverride ?? Path.Combine(Application.persistentDataPath, "ResearchSessions");
        public CalibrationWizard Wizard { get; } = new CalibrationWizard();
        public PoseFrame Frame { get; private set; }
        public IPoseSource Source { get; private set; }
        public bool Active { get; private set; }
        public bool PresentationReady;
        public bool TrainingEnded { get; private set; }
        public string Status { get; private set; } = "停止中";
        public string TrialId { get; private set; }
        public TrialSelection Selection = new TrialSelection();
        public string ParticipantId, VisitId;
        public StudyGroup Group;
        public float Volume = .1f;
        public FeedbackSettings Settings = new FeedbackSettings();
        public event Action<string> TrialClosed;
        public event Action<string> Interrupted;
        ResearchSessionLog log; WheelSpeedEstimator estimator; SpeedFeedbackEngine feedback;
        double flushAt, previousTime; bool wasValid;
        public void SetSource(IPoseSource source)
        {
            if (Active) throw new InvalidOperationException("記録中は入力を変更できません。");
            Source?.Dispose(); Source = source; Wizard.Reset();
            if (source is SyntheticPoseSource) Wizard.UseSynthetic(SyntheticPoseSource.Calibration());
        }
        public string BeginTrial()
        {
            if (Active) throw new InvalidOperationException("既に記録中です。");
            if (!PresentationReady || Source == null || !Wizard.Calibration.Complete) throw new InvalidOperationException("提示・入力・校正の準備を確認してください。");
            if (Selection.task == StudyTask.None || Selection.phase == StudyPhase.Rest) throw new InvalidOperationException("記録する時点・課題を選択してください。");
            var frame = Source.Read(Time.realtimeSinceStartupAsDouble);
            var bindingError = Wizard.Calibration.BindingError(frame); if (bindingError != null) throw new InvalidOperationException(bindingError);
            TrialId = Guid.NewGuid().ToString("N");
            var settings = new ResearchSessionSettings { participant_id = ParticipantId, visit_id = VisitId, trial_id = TrialId, phase = Selection.phase.ToString(), task = Selection.task.ToString(), repetition = Selection.repetition,
                group = Group.ToString(), input_source = Source.Kind, feedback = Settings, digital_volume = Volume, created_utc = DateTime.UtcNow.ToString("O"), application_version = Application.version, unity_version = Application.unityVersion,
                source_commit = Resources.Load<TextAsset>("BuildIdentity")?.text ?? "development_worktree" };
            log = new ResearchSessionLog(SaveRoot, settings, Wizard.Calibration);
            estimator = new WheelSpeedEstimator(Wizard.Calibration, Settings.maximumGapSeconds); feedback = new SpeedFeedbackEngine(Settings);
            Output.Volume = Volume; Active = true; TrainingEnded = false; wasValid = false;
            previousTime = Time.realtimeSinceStartupAsDouble; flushAt = previousTime + .5;
            log.Event(previousTime, "record_started"); Status = "記録中"; return TrialId;
        }
        void Update() => Tick(Time.realtimeSinceStartupAsDouble);
        public void Tick(double time)
        {
            if (Source == null) return;
            try
            {
                Frame = Source.Read(time); if (Frame == null) { if (Active) Interrupt("pose_source_ended"); return; }
                Wizard.Observe(Frame);
                if (!Active) return;
                log.Poses(Frame, Wizard.Calibration);
                var sample = estimator.Update(Frame);
                var value = feedback.Update(Frame.time, sample.leftRps, sample.rightRps, sample.valid, Group, Selection.FeedbackAllowed && !TrainingEnded);
                if (wasValid != sample.valid) log.Event(time, sample.valid ? "critical_tracking_recovered" : "critical_tracking_missing", sample.reason);
                wasValid = sample.valid;
                Output.Apply(value); log.Speed(Frame.time, sample, value); log.Diagnostic(time, time - previousTime); previousTime = time;
                if (time >= flushAt) { FileStorage.EnsureReserve(SaveRoot); log.Flush(); flushAt = time + FeedbackProtocol.FlushIntervalSeconds; }
            }
            catch (Exception e) { Interrupt("storage_or_input_failure:" + e.Message); }
        }
        public void StopTraining()
        {
            if (!Active || Selection.phase != StudyPhase.Training) throw new InvalidOperationException("訓練記録中に使います。");
            TrainingEnded = true; Output.Stop(); log.Event(Time.realtimeSinceStartupAsDouble, "training_feedback_ended");
        }
        public void EndTrial(bool complete = true, string reason = "operator_record_end")
        {
            if (!Active) return;
            Active = false; Output.Stop(); string path = log.DirectoryPath;
            try { log.Complete(Time.realtimeSinceStartupAsDouble, complete, reason); Status = complete ? "記録終了" : "未完了：" + reason; }
            catch (Exception e) { log.Dispose(); Status = "保存失敗・元記録を保持：" + e.Message; Interrupted?.Invoke("save_failure"); }
            log = null; TrialClosed?.Invoke(path);
        }
        public void Interrupt(string reason) { if (!Active) { Output?.Stop(); return; } EndTrial(false, reason); Interrupted?.Invoke(reason); }
        public void InvalidateCalibration(string reason) { Wizard.Calibration.Invalidate(reason); Output?.Stop(); }
        void OnDestroy() { Interrupt("application_closed"); Source?.Dispose(); }
    }
}
