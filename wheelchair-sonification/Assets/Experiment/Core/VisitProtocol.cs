using System;
using System.Collections.Generic;

namespace WheelchairSonification.Core
{
    [Serializable]
    public sealed class VisitStep
    {
        public string id, title, kind, instruction;
        public StudyPhase phase;
        public StudyTask task;
        public int repetition, seconds;
        public VisitStep(string id, string title, string kind, string instruction, StudyPhase phase = StudyPhase.Preparation,
            StudyTask task = StudyTask.None, int repetition = 0, int seconds = 0)
        { this.id = id; this.title = title; this.kind = kind; this.instruction = instruction; this.phase = phase;
          this.task = task; this.repetition = repetition; this.seconds = seconds; }
    }

    [Serializable]
    public sealed class VisitState
    {
        public int schema = 1, participantNumber = 1, enrollmentNumber = 1, step, revision;
        public string visitId, createdUtc, status = "ready", group = "unallocated", lastReason, activeTrialId, retryOfTrialId;
        public bool majorRight = true, targetReady, equipmentReady, comprehensionPassed, recoveryRequired, restReview, turnaround, questionnaireRecorded;
        public float volume = .1f;
        public double elapsed, stepElapsed, restVerifiedSeconds;
        public int[] technicalRetries = new int[3];
        public List<string> appliedCommands = new List<string>();
        public List<VisitTrial> trials = new List<VisitTrial>();
        public ComprehensionState quiz = new ComprehensionState();
    }
    [Serializable]
    public sealed class VisitTrial
    {
        public string trialId, stepId, replacesTrialId, reason;
        public bool completed;
        public double elapsed;
    }
    [Serializable]
    public sealed class ComprehensionState
    {
        public int modality, question, attempt, correct, seenStates;
        public bool started, awaitingAnswer, passed, failed;
        public double elapsed, nextDue;
        public List<ComprehensionAnswer> answers = new List<ComprehensionAnswer>();
    }
    [Serializable]
    public sealed class ComprehensionAnswer
    {
        public string modality, response, correction;
        public int question, attempt, errorDegrees;
        public bool correct, timeout;
        public double elapsed;
    }

    // This is the executable schedule. Human-facing definitions remain in experiment-protocol.md.
    public static class VisitProtocol
    {
        public const double OperatorTimeout = 3, HeartbeatSeconds = .5, RestSeconds = 1200, VisitSeconds = 5400;
        public static readonly VisitStep[] Steps = Make();
        public static readonly int[] AudioErrors = { -12, 0, 12, -3, 8, -8, 3, -10, 0, 10 };
        public static readonly int[] VisualErrors = { 0, 8, -8, 3, -10, 10, -3, 12, 0, -12 };
        private static VisitStep[] Make()
        {
            var s = new List<VisitStep> {
                new VisitStep("consent", "説明・同意・適格性", "manual", "承認済み書類で同意・適格性・体調を確認してください。実名は入力しません。"),
                new VisitStep("equipment", "装着・校正", "equipment", "椅子適合、5台の役割、校正、視界、快適音量、保存を確認してください。"),
                new VisitStep("familiar-straight", "共通習熟：直進", "trial", "追加FBなしで直進と安全な停止を説明してください。", StudyPhase.Preparation, StudyTask.Straight, 1),
                new VisitStep("familiar-f8", "共通習熟：8の字", "trial", "追加FBなしで左→右の1周と停止を確認してください。", StudyPhase.Preparation, StudyTask.FigureEight, 1) };
            Assess(s, "pre", "事前評価", StudyPhase.Baseline);
            s.Add(new VisitStep("target", "個人目標の確認", "target", "事前直進2反復の有効本数と固定目標を確認してください。不足では割付へ進めません。"));
            s.Add(new VisitStep("comprehension", "音と表示の理解確認", "quiz", "座位・静止で口頭回答を採点します。正答は被験者へ表示しません。"));
            s.Add(new VisitStep("allocation", "割付の登録", "allocation", "割付表を順に開示し、A・V・Nの群を登録してください。登録後は変更できません。"));
            for (int i = 1; i <= 3; i++)
            {
                s.Add(new VisitStep("train-straight-" + i, "訓練 " + i + "/3：直進", "trial", "快適な速度で往復。2分後は新しい走行を始めず安全に終了。", StudyPhase.Training, StudyTask.Straight, i, 120));
                s.Add(new VisitStep("switch-" + i, "切替：30秒", "interval", "8の字の位置へ安全に移動・整列してください。", seconds: 30));
                s.Add(new VisitStep("train-f8-" + i, "訓練 " + i + "/3：8の字", "trial", "快適な速度で繰り返します。2分後は新しい周回を始めず安全に終了。", StudyPhase.Training, StudyTask.FigureEight, i, 120));
                s.Add(new VisitStep("move-" + i, "終了・移動：30秒", "interval", "安全な停止・位置戻しを行ってください。", seconds: 30));
                if (i < 3) s.Add(new VisitStep("set-rest-" + i, "セット間休憩：2分", "interval", "座位で休憩。追加練習や動作の助言をしません。", seconds: 120));
            }
            Assess(s, "post", "直後評価", StudyPhase.ImmediateAssessment);
            s.Add(new VisitStep("questionnaire", "訓練全体の質問紙", "questionnaire", "用紙または口頭で回収。間に合わなければ理由を記録し省略します。"));
            s.Add(new VisitStep("rest", "保持前休憩：20分", "rest", "休憩開始を確認。静かな座位で20分。追加練習・FB・動作教示なし。", StudyPhase.Rest, seconds: 1200));
            Assess(s, "retention", "短時間保持評価", StudyPhase.RetentionAssessment);
            s.Add(new VisitStep("finish", "取り外し・体調・記録確認", "manual", "体調、動画、試行件数、保存・回収を確認して終了してください。"));
            s.Add(new VisitStep("done", "来訪終了", "done", "記録を回収し、アクセス制限のある保管先で照合してください。"));
            return s.ToArray();
        }
        private static void Assess(List<VisitStep> s, string id, string title, StudyPhase p)
        {
            foreach (var task in new[] { StudyTask.FigureEight, StudyTask.Straight })
                for (int r = 1; r <= 2; r++) s.Add(new VisitStep(id + "-" + task + "-" + r,
                    title + "：" + (task == StudyTask.FigureEight ? "8の字" : "直進") + " " + r + "/2", "trial",
                    "共通教示・開始位置・動画を確認し、安全な開始を指示してください。反復間は原則30秒休憩。", p, task, r));
        }
        public static bool Technical(string reason) => reason == "tracking_lost" || reason == "connection_lost" ||
            reason == "save_failure" || reason == "video_failure" || reason == "setup_failure" || reason == "calibration_invalid" ||
            reason == "application_restart" || reason == "application_pause" || reason == "application_focus_lost" || reason == "xr_or_head_tracking_lost";
        public static bool Safety(string reason) => reason == "pain" || reason == "participant_stop" || reason == "safety_intervention" || reason == "visit_time_limit" || reason == "elapsed_time_unverified";
    }

    public sealed class VisitEngine
    {
        public VisitState State { get; }
        public VisitStep Current => VisitProtocol.Steps[State.step];
        public double Remaining => Math.Max(0, (State.turnaround ? 30 : Current.seconds) - State.stepElapsed);
        public bool CanRetry => State.status == "interrupted" && !State.recoveryRequired && PhaseIndex() >= 0 &&
            VisitProtocol.Technical(State.lastReason) && State.technicalRetries[PhaseIndex()] == 0;
        public event Action<StudyGroup, int> Cue;
        private readonly IClock clock;
        private double last;
        public VisitEngine(IClock clock, VisitState state)
        { this.clock = clock; State = state; last = clock.Now;
          if (state.step < 0 || state.step >= VisitProtocol.Steps.Length) throw new ArgumentException("来訪の手順が不正です。"); }

        public void Tick()
        {
            double dt = Math.Max(0, clock.Now - last); last = clock.Now;
            if (State.status == "done" || State.status == "terminated") return;
            State.elapsed += dt;
            if (State.status == "timer" || State.status == "active" || State.status == "finishing" || State.status == "turnaround") State.stepElapsed += dt;
            if (State.elapsed >= VisitProtocol.VisitSeconds && State.status != "interrupted")
            { Interrupt("visit_time_limit"); return; }
            if (State.status == "timer" && Remaining <= 0 && !State.restReview)
            {
                if (Current.kind == "interval") MoveNext();
                else { State.status = "ready"; State.revision++; }
            }
            if (State.status == "turnaround" && Remaining <= 0)
            { State.turnaround = false; State.stepElapsed = 0; State.status = "ready"; State.revision++; }
            TickQuiz(dt);
        }
        public void StartStep()
        {
            Require(State.status == "ready" && !State.recoveryRequired, "停止・復旧確認が必要です。");
            Require(State.elapsed < VisitProtocol.VisitSeconds, "90分の上限に達しました。");
            switch (Current.kind)
            {
                case "equipment": Require(State.equipmentReady, "5台・校正・視界・保存を確認してください。"); MoveNext(); break;
                case "target": Require(State.targetReady, "基準量不足では進めません。"); MoveNext(); break;
                case "allocation": throw new InvalidOperationException("割付表の群を登録してください。");
                case "quiz": Require(!State.quiz.failed, "理解確認未達。割付へ進めません。");
                    if (State.comprehensionPassed) MoveNext(); else StartQuiz(); break;
                case "trial": State.status = "active"; State.stepElapsed = 0; State.revision++; break;
                case "rest": Require(!State.restReview, "中断前の20分休憩の成立確認が必要です。");
                    if (State.stepElapsed >= Current.seconds) MoveNext();
                    else { State.status = "timer"; State.stepElapsed = 0; State.revision++; } break;
                case "interval": throw new InvalidOperationException("切替・休憩時間が残っています。");
                case "questionnaire": Require(State.questionnaireRecorded, "回答または省略理由の保存が必要です。"); MoveNext(); break;
                case "done": break;
                default: MoveNext(); break;
            }
        }
        public void Assign(StudyGroup group)
        {
            Require(Current.kind == "allocation" && State.targetReady && State.comprehensionPassed && State.group == "unallocated", "目標・理解確認後に一度だけ割付できます。");
            State.group = group.ToString(); MoveNext();
        }
        public void FinishTrial(string trialId)
        {
            Require(Current.kind == "trial" && (State.status == "active" || State.status == "finishing"), "実行中の試行がありません。");
            State.trials.Add(new VisitTrial { trialId = trialId, stepId = Current.id, completed = true,
                replacesTrialId = State.retryOfTrialId, elapsed = State.stepElapsed });
            State.activeTrialId = null; MoveNext();
        }
        public void Interrupt(string reason)
        {
            if (State.status == "done" || State.status == "terminated") return;
            State.lastReason = reason; State.recoveryRequired = true;
            if (Current.kind == "rest") State.restReview = true;
            State.status = "interrupted"; State.revision++;
        }
        public void Recover()
        {
            Require(State.status == "interrupted", "復旧対象がありません。");
            Require(!VisitProtocol.Safety(State.lastReason), "安全・参加中止では続行できません。");
            Require(State.lastReason != "operator_stop", "停止理由を確定してから復旧を確認してください。");
            State.recoveryRequired = false;
            if (Current.kind != "trial" || State.turnaround) { State.status = State.turnaround ? "turnaround" : "ready"; State.revision++; }
        }
        public void ReviewStopReason(string reason)
        {
            Require(State.status == "interrupted" && State.recoveryRequired, "復旧前の中断だけ理由を照合できます。");
            Require(!VisitProtocol.Safety(State.lastReason) || reason == State.lastReason, "安全中止や時間・時計の不成立を技術理由へ変更できません。");
            Require(VisitProtocol.Technical(reason) || VisitProtocol.Safety(reason) || reason == "path_error" || reason == "operator_stop", "中断理由が不正です。");
            State.lastReason = reason; State.revision++;
        }
        public void Retry()
        {
            int phase = PhaseIndex();
            Require(State.status == "interrupted" && !State.recoveryRequired && phase >= 0 &&
                VisitProtocol.Technical(State.lastReason) && State.technicalRetries[phase] == 0, "技術的再試行は各評価時点で追加1回までです。");
            State.retryOfTrialId = State.activeTrialId; State.technicalRetries[phase]++; State.status = "ready"; State.stepElapsed = 0; State.revision++;
        }
        public void Skip()
        {
            Require(State.status == "interrupted" && !State.recoveryRequired && !VisitProtocol.Safety(State.lastReason), "停止・復旧を確認してください。");
            MoveNext();
        }
        public void VerifyRest(double seconds)
        {
            Require(Current.kind == "rest" && State.restReview && !State.recoveryRequired && seconds >= 1200 && double.IsFinite(seconds), "外部記録で20分以上を確認できる場合のみ登録できます。");
            State.restVerifiedSeconds = seconds; State.stepElapsed = seconds; State.restReview = false; State.revision++;
        }
        public void Terminate(string reason) { State.lastReason = reason; State.status = "terminated"; State.revision++; }
        private int PhaseIndex() => Current.phase == StudyPhase.Baseline ? 0 : Current.phase == StudyPhase.ImmediateAssessment ? 1 : Current.phase == StudyPhase.RetentionAssessment ? 2 : -1;
        private void MoveNext()
        {
            var previous = Current;
            State.step = Math.Min(State.step + 1, VisitProtocol.Steps.Length - 1); State.stepElapsed = 0;
            State.turnaround = previous.kind == "trial" && (previous.phase == StudyPhase.Baseline || previous.phase == StudyPhase.ImmediateAssessment || previous.phase == StudyPhase.RetentionAssessment) && Current.kind == "trial" && Current.phase == previous.phase;
            State.status = State.turnaround ? "turnaround" : Current.kind == "done" ? "done" : Current.kind == "interval" ? "timer" : "ready";
            State.lastReason = null; State.activeTrialId = null; State.retryOfTrialId = null; State.recoveryRequired = false; State.revision++;
        }
        public StudyGroup QuizModality => ((State.enrollmentNumber % 2 == 1 ? 0 : 1) + State.quiz.modality) % 2 == 0 ? StudyGroup.Auditory : StudyGroup.Visual;
        private void StartQuiz()
        {
            var q = State.quiz; q.started = true; q.elapsed = 0; q.nextDue = 0; q.awaitingAnswer = false;
            State.status = "quiz"; State.revision++;
        }
        public int QuizError
        {
            get { var q = State.quiz; int index = q.attempt == 0 ? q.question : 9 - q.question;
                return (QuizModality == StudyGroup.Auditory ? VisitProtocol.AudioErrors : VisitProtocol.VisualErrors)[index]; }
        }
        private void TickQuiz(double dt)
        {
            if (Current.kind != "quiz" || State.status != "quiz") return;
            var q = State.quiz; q.elapsed += dt;
            if (q.awaitingAnswer && q.elapsed >= q.nextDue) Answer("", "", true);
            else if (!q.awaitingAnswer && q.elapsed >= q.nextDue)
            { q.awaitingAnswer = true; q.nextDue = q.elapsed + 5; State.revision++; Cue?.Invoke(QuizModality, QuizError); }
        }
        public void Answer(string response, string correction, bool timeout = false)
        {
            var q = State.quiz;
            Require(State.status == "quiz" && q.awaitingAnswer, "回答待ちではありません。");
            int error = QuizError; var expected = error < -5 ? FeedbackState.Short : error > 5 ? FeedbackState.Long : FeedbackState.Target;
            bool correct = !timeout && response == expected.ToString() && correction == (expected == FeedbackState.Short ? "longer" : expected == FeedbackState.Long ? "shorter" : "maintain");
            q.answers.Add(new ComprehensionAnswer { modality = QuizModality.ToString(), question = q.question + 1,
                attempt = q.attempt + 1, errorDegrees = error, response = response, correction = correction, correct = correct, timeout = timeout, elapsed = q.elapsed });
            if (correct) { q.correct++; q.seenStates |= 1 << (int)expected; }
            q.awaitingAnswer = false; q.question++; q.nextDue = q.elapsed + 1; State.revision++;
            if (q.question < 10) return;
            bool pass = q.correct >= 9 && q.seenStates == 7;
            if (!pass && q.attempt == 1) { q.failed = true; State.status = "ready"; return; }
            if (!pass) { q.attempt = 1; State.status = "ready"; }
            else if (q.modality == 0) { q.modality = 1; q.attempt = 0; State.status = "ready"; }
            else { q.passed = State.comprehensionPassed = true; State.status = "ready"; }
            q.question = q.correct = q.seenStates = 0;
        }
        private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    }
}
