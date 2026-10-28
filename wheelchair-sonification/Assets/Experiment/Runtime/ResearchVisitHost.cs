using System;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    [Serializable] public sealed class OperatorCommand { public string id, visitId, action, value; public int revision; public double number; public bool flag; }
    [Serializable] public sealed class OperatorResult { public string id, message; public bool ok; }
    [Serializable] public sealed class DeviceSummary { public string key, product; public bool valid; public int runtimeDeviceId; public float x,y,z,rx,ry,rz; }
    [Serializable] public sealed class OperatorSnapshot
    {
        public int schema = 3, revision;
        public string visitId, participantId, trialId, group, sourceKind, phase, task, status, calibrationInstruction, exportStatus, error,presentationStatus;
        public int repetition;
        public bool running, calibrationReady, presentationReady, trainingEnded, transferring;
        public DeviceSummary[] devices; public string[] bindings;
        public PreparationSnapshot preparation;
    }
    public sealed class ResearchVisitHost : MonoBehaviour
    {
        public ResearchController Research { get; private set; }
        public PreparationWorkflow Preparation { get; private set; }
        public Func<string, IPoseSource> SourceFactory;
        public string RootOverride;
        public string VisitRoot => RootOverride ?? Path.Combine(Application.persistentDataPath, "ResearchVisits");
        public string CheckRoot => RootOverride == null ? Path.Combine(Application.persistentDataPath,"DeviceChecks") : Path.Combine(RootOverride,"DeviceChecks");
        public string ExportPath { get; private set; }
        public string ExportStatus { get; private set; } = "未回収";
        public string Error { get; private set; }
        public bool Transferring { get; private set; }
        public int Revision { get; private set; }
        public Action<StudyGroup,double,double> Present;
        public Action StopPresentation;
        public IExportDestination UsbDestinationOverride;
        string visitId, participantId, exportTrialPath; bool groupAssigned, recoveryPaused;
        readonly List<string> pendingTrials = new List<string>();
        readonly HashSet<string> recoveredTrials = new HashSet<string>(StringComparer.Ordinal);
        double leaseUntil;
        OperatorResult lastResult;
        readonly Dictionary<string,OperatorResult> results=new Dictionary<string,OperatorResult>();
        public void Initialize(ResearchController research)
        {
            Research = research;
            Research.TrialClosed += path => { Preparation?.RequireStop();QueueTrial(path); Save(); };
            Research.Interrupted += reason => { Error = reason; Revision++; Save(); };
            Preparation=new PreparationWorkflow(Research.Wizard,()=>CheckRoot,()=>Research.Source?.Kind??"none",value=>{Research.Output.Volume=Research.Volume;Research.Output.Apply(value);},()=>Research.Output?.Stop());
            Preparation.Changed+=()=>{Revision++;Save();};Preparation.Closed+=QueueTrial;
        }
        public void ObserveOperator(bool connected, double leaseSeconds) { if (connected) leaseUntil = Time.realtimeSinceStartupAsDouble + Math.Min(3, Math.Max(0, leaseSeconds)); }
        void Update()
        {
            if(Research==null)return;
            if(Research.Active&&Time.realtimeSinceStartupAsDouble>=leaseUntil)Suspend("operator_connection_lost");
            if(!Research.Active)Preparation.Tick(Research.Frame,Time.realtimeSinceStartupAsDouble,Research.PresentationReady,Research.Settings,Research.Volume);
        }
        public void Suspend(string reason) { StopPresentation?.Invoke(); Research?.Interrupt(reason);Preparation?.Interrupt(reason); Error = reason; }
        public OperatorResult ExecuteLocal(string action,string value="",int number=0)=>Execute(new OperatorCommand{id=Guid.NewGuid().ToString("N"),visitId=visitId??"",revision=Revision,action=action,value=value,number=number});
        public void SetInput(string kind)
        {
            if (Research.Active || Transferring) throw new InvalidOperationException("停止してから入力を変更してください。");
            if (kind == "synthetic") Research.SetSource(new SyntheticPoseSource());
            else if (kind == "actual" && SourceFactory != null) Research.SetSource(SourceFactory(kind));
            else throw new InvalidOperationException("この環境では実測入力を開始できません。");
        }
        public OperatorResult Execute(OperatorCommand c)
        {
            if (c.id!=null&&results.TryGetValue(c.id,out var cached)) return cached;
            var result = new OperatorResult { id = c.id };
            try
            {
                if ((c.visitId != (visitId ?? "") || c.revision != Revision) && c.action != "abort" && c.action!="prep.cancel") throw new InvalidOperationException("画面が更新されました。最新の状態で選び直してください。");
                if (Transferring && c.action != "abort" && c.action!="prep.cancel") throw new InvalidOperationException("転送結果を待ってください。");
                if (Research.Active && c.action != "finishTrial" && c.action != "trainingEnd" && c.action != "abort") throw new InvalidOperationException("記録中に設定は変更できません。");
                if(Preparation.Active&&!c.action.StartsWith("prep.",StringComparison.Ordinal)&&c.action!="abort")throw new InvalidOperationException("準備チェックを終了してから実験の操作をしてください。");
                if(c.action.StartsWith("prep.",StringComparison.Ordinal))
                {
                    if((c.action=="prep.start"||c.action=="prep.recheck")&&!Research.PresentationReady)throw new InvalidOperationException(Research.PresentationStatus);
                    Error=null;StopPresentation?.Invoke();Preparation.Execute(c.action.Substring(5),c.value,(int)c.number);
                    Revision++;Save();result.ok=true;result.message="準備画面へ反映しました。";Remember(result);return result;
                }
                switch (c.action)
                {
                    case "createVisit":
                        if (Research.Active) throw new InvalidOperationException("記録を閉じてください。");
                        if (!System.Text.RegularExpressions.Regex.IsMatch(c.value ?? "", "^[A-Za-z0-9_-]{1,40}$")) throw new ArgumentException("実名を使わず英数字の研究IDを入力してください。");
                        visitId = Guid.NewGuid().ToString("N"); participantId = c.value; Research.VisitId = visitId; Research.ParticipantId = participantId; groupAssigned = false; Research.Group = StudyGroup.NoFeedback; break;
                    case "setInput": SetInput(c.value); break;
                    case "setGroup": Research.Group = (StudyGroup)Enum.Parse(typeof(StudyGroup), c.value); groupAssigned = true; break;
                    case "setSelection":
                        var selection = JsonUtility.FromJson<TrialSelection>(c.value);
                        if (selection == null || !Enum.IsDefined(typeof(StudyPhase), selection.phase) || !Enum.IsDefined(typeof(StudyTask), selection.task) || selection.repetition < 1 || selection.repetition > 99) throw new ArgumentException("時点・課題・反復を確認してください。");
                        Research.Selection = selection; break;
                    case "start": if (string.IsNullOrEmpty(visitId)) throw new InvalidOperationException("来訪を作成してください。"); if (Research.Selection.phase == StudyPhase.Training && !groupAssigned) throw new InvalidOperationException("訓練前に割付群を登録してください。"); Error = null; StopPresentation?.Invoke(); Research.BeginTrial(); break;
                    case "finishTrial": Research.EndTrial(); break;
                    case "trainingEnd": Research.StopTraining(); break;
                    case "abort": Suspend(string.IsNullOrEmpty(c.value) ? "operator_abort" : c.value); break;
                    case "assignRole": if(string.IsNullOrEmpty(c.value))Research.Wizard.Unbind((TrackerRole)(int)c.number);else Research.Wizard.Bind((TrackerRole)(int)c.number, Research.Frame?.Find(c.value)); break;
                    case "calibrate": Research.Wizard.Capture(Research.Frame); break;
                    case "resetCalibration": Research.Wizard.Reset(); break;
                    case "setVolume": if (c.number < 0 || c.number > .3 || !double.IsFinite(c.number)) throw new ArgumentException("音量の範囲は0–0.3です。"); Research.Volume = (float)c.number; break;
                    case "quick":
                        if(!Research.PresentationReady)throw new InvalidOperationException(Research.PresentationStatus);
                        var parts = c.value.Split(':'); var group = (StudyGroup)Enum.Parse(typeof(StudyGroup), parts[0]);
                        if (parts.Length != 3) throw new ArgumentException("見本を選んでください。");
                        Present?.Invoke(group, double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "export": FindUnrecoveredTrials(); recoveryPaused = false; PrepareNextTransfer(); break;
                    case "exportUsb": StartCoroutine(ExportUsb()); break;
                    default: throw new ArgumentException("未対応の操作です。");
                }
                if(c.action!="abort")Error=null;Revision++; Save(); result.ok = true; result.message = "反映しました。";
            }
            catch (Exception e) { result.ok = false; result.message = e.Message; Error = e.Message; }
            Remember(result); return result;
        }
        void Remember(OperatorResult result){lastResult=result;if(results.Count>=128)results.Remove(results.Keys.First());if(!string.IsNullOrEmpty(result.id))results[result.id]=result;}
        IEnumerator ExportUsb()
        {
            Transferring = true; ExportStatus = "Downloadへ書き出し中";
            string root = Research.SaveRoot, staging = Path.Combine(Application.persistentDataPath, "ExportStaging");
            IExportDestination destination = UsbDestinationOverride ?? new AndroidDownloadExport();
            // Android JNI attach/detach belongs to this worker, never to Unity's main thread.
            var task = Task.Run(() => { string zip = SessionExporter.CreateResearchBundle(root,VisitRoot,staging,CheckRoot); return destination.Write(zip, Path.GetFileName(zip)); });
            while (!task.IsCompleted) yield return null;
            Transferring = false;
            ExportStatus = task.IsFaulted ? "Download書き出し失敗・元記録保持：" + task.Exception.GetBaseException().Message : task.Result;
        }
        void FindUnrecoveredTrials()
        {
            if (Directory.Exists(Research.SaveRoot))
                foreach (string path in Directory.GetDirectories(Research.SaveRoot).Where(p => File.Exists(Path.Combine(p, "session.json"))).OrderBy(Directory.GetCreationTimeUtc))
                    if (!recoveredTrials.Contains(path) && !pendingTrials.Contains(path)) pendingTrials.Add(path);
            if(Directory.Exists(CheckRoot))foreach(string path in Directory.GetDirectories(CheckRoot).Where(p=>File.Exists(Path.Combine(p,"report.json"))))if(!recoveredTrials.Contains(path)&&!pendingTrials.Contains(path))pendingTrials.Add(path);
            if (pendingTrials.Count == 0) throw new InvalidOperationException("未回収の記録がありません。再起動後の再回収では既存のZIPと重複する場合があります。");
        }
        void QueueTrial(string path)
        {
            if (path == null) throw new InvalidOperationException("回収する記録がありません。");
            if (!recoveredTrials.Contains(path) && !pendingTrials.Contains(path)) pendingTrials.Add(path);
            PrepareNextTransfer();
        }
        void PrepareNextTransfer()
        {
            if (ExportPath != null || recoveryPaused || pendingTrials.Count == 0) return;
            try { exportTrialPath = pendingTrials[0];var staging=Path.Combine(Application.persistentDataPath,"ExportStaging");ExportPath = File.Exists(Path.Combine(exportTrialPath,"report.json"))?SessionExporter.CreateCheckArchive(exportTrialPath,staging):SessionExporter.CreateTrialArchive(exportTrialPath,staging); ExportStatus = "記録コピー待ち・未照合 " + pendingTrials.Count + "件"; }
            catch (Exception e) { ExportPath = null; recoveryPaused = true; ExportStatus = "ZIP失敗・元データ保持：" + e.Message; }
        }
        public bool BeginTransfer() { if (Transferring || Research.Active || Preparation.Active) return false; Transferring = true; return true; }
        public void TransferFinished(bool ok, string message)
        {
            Transferring = false;
            if (ok && exportTrialPath != null) { recoveredTrials.Add(exportTrialPath); pendingTrials.Remove(exportTrialPath); }
            recoveryPaused = !ok;
            ExportStatus = ok ? "PCへコピー・SHA-256照合済み。元記録を保持。" : "転送失敗・未照合 " + pendingTrials.Count + "件。再回収できます：" + message;
            // Retry is explicit after a failed transfer; don't loop a failed upload every 500 ms.
            ExportPath = null; exportTrialPath = null;
            if (ok) PrepareNextTransfer();
        }
        void Save()
        {
            if (string.IsNullOrEmpty(visitId)) return;
            try { FileStorage.EnsureReserve(VisitRoot); string file = Path.Combine(VisitRoot, visitId + ".json"); File.WriteAllText(file + ".partial", JsonUtility.ToJson(Snapshot(), true)); if (File.Exists(file)) File.Delete(file); File.Move(file + ".partial", file); }
            catch (Exception e) { Error = "visit_save_failure:" + e.Message; Research.Interrupt("visit_save_failure"); }
        }
        public OperatorSnapshot Snapshot() => new OperatorSnapshot { revision = Revision, visitId = visitId ?? "", participantId = participantId ?? "", trialId = Research.TrialId ?? "", group = groupAssigned ? Research.Group.ToString() : "unallocated", sourceKind = Research.Source?.Kind ?? "none",
            phase = Research.Selection.phase.ToString(), task = Research.Selection.task.ToString(), repetition = Research.Selection.repetition,
            running = Research.Active, status = Research.Status, error = Error ?? "", trainingEnded = Research.TrainingEnded,
            calibrationReady = Research.Wizard.Calibration.Complete && Research.Wizard.Calibration.BindingError(Research.Frame)==null, calibrationInstruction = Research.Wizard.Instruction, presentationReady = Research.PresentationReady,presentationStatus=Research.PresentationStatus, exportStatus = ExportStatus, transferring = Transferring,
            devices = Research.Frame?.poses.Select(p => new DeviceSummary { key = p.key, product = p.product, valid = p.valid, runtimeDeviceId = p.runtimeDeviceId, x=p.position.x, y=p.position.y, z=p.position.z, rx=p.rotation.eulerAngles.x, ry=p.rotation.eulerAngles.y, rz=p.rotation.eulerAngles.z }).ToArray() ?? Array.Empty<DeviceSummary>(),
            bindings = Research.Wizard.Calibration.roles.Select(r => r.key ?? "").ToArray(), preparation=Preparation.Snapshot() };
        void OnDestroy(){Preparation?.Dispose();}
    }
}
