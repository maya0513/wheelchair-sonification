using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    [Serializable] public sealed class PreparationAction { public string id,label; public PreparationAction(string id,string label){this.id=id;this.label=label;} }
    [Serializable] public sealed class CheckItem { public string id,label,status="pending",detail="未実施";public double validRatio,evidenceSeconds; }
    [Serializable] public sealed class PreparationSnapshot
    {
        public string checkId,stage,title,instruction,detail,problem,technicalProblem,directory,scope;
        public bool active,moving,showLabels,safeToShowPanel,physicalAccuracyVerified=false;
        public int manualRole=-1,requiredTracked,wristTracked,step;
        public string[] proposedBindings;
        public PreparationAction[] actions;
        public CheckItem[] results;
        public double leftRps,rightRps;
        public bool speedValid;
        public PreparationSettings settings;
    }

    // One state owner for the headset and PC. Clock and frames are supplied, so tests never wait.
    public sealed class PreparationWorkflow : IDisposable
    {
        readonly CalibrationWizard wizard;
        readonly Func<string> root;
        readonly Func<string> sourceKind;
        readonly Action<BilateralFeedback> present;
        readonly Action stop;
        readonly List<PoseFrame> forward=new List<PoseFrame>(),backward=new List<PoseFrame>(),segment=new List<PoseFrame>();
        readonly Dictionary<string,CheckItem> results=new Dictionary<string,CheckItem>();
        public readonly PreparationSettings Settings=new PreparationSettings();
        public event Action Changed;
        public event Action<string> Closed;
        public string Stage {get;private set;}="Home";
        public string Problem {get;private set;}="";
        public string TechnicalProblem {get;private set;}="";
        public bool Active => Stage!="Home"&&Stage!="Result"&&Stage!="Waiting";
        public bool Moving => Stage=="Countdown"||Stage=="Stopping"||Stage=="Still"||IsMotion(Stage)||Stage=="WristLeft"||Stage=="WristRight";
        public bool ShowLabels => Stage=="Candidate"||Stage=="Manual";
        public int ManualRole {get;private set;}=2;
        public PreparationCandidate Candidate {get;private set;}
        public PreparationSnapshot View => Snapshot();
        DeviceCheckLog log;
        PoseFrame frame,previous;
        WheelSpeedEstimator estimator;
        SpeedFeedbackEngine engine;
        WheelSpeedSample speed;
        string id="",directory="",nextMotion="",manualNext="",sourceAtStart="";
        string scope="full";
        string[] proposed=new string[5];
        double now,entered,stillSince=double.NaN,uiStillSince=double.NaN,lastSample=double.NegativeInfinity,motionEvidence,lastSpeedTime;
        bool manual,observed;
        int speedSamples,validSpeedSamples;

        public PreparationWorkflow(CalibrationWizard wizard,Func<string> root,Func<string> sourceKind,Action<BilateralFeedback> present,Action stop)
        {this.wizard=wizard;this.root=root;this.sourceKind=sourceKind;this.present=present;this.stop=stop;}
        static bool IsMotion(string stage)=>stage=="Forward"||stage=="Backward"||stage.StartsWith("Verify",StringComparison.Ordinal)&&!stage.EndsWith("Ready",StringComparison.Ordinal);
        public void Execute(string action,string value="",int role=-1)
        {
            if(action=="start"){Start();return;}
            if(action=="recheck"){Start(value);return;}
            if(action=="settings"){Configure(value);return;}
            if(action=="home"&&!Active){Enter("Home");return;}
            log?.Action(now,action,value);
            if(action=="cancel"){Finish(false,"実施者がチェックを中断しました。");return;}
            if(action=="wait"&&(!Active||Stage=="Result")){Enter("Waiting");return;}
            if(action=="manual"&&!Moving&&Active){manual=true;proposed=new string[5];ManualRole=2;manualNext="calibrate";Enter("Manual");return;}
            if(action=="select"&&Stage=="Manual")
            {
                if(role!=ManualRole)throw new InvalidOperationException("現在案内している役割を選んでください。");
                var p=frame?.Find(value);if(p?.valid!=true)throw new InvalidOperationException("追跡中のトラッカーを選んでください。");
                if(proposed.Where((_,i)=>i!=role).Contains(value))throw new InvalidOperationException("別の役割で選んだ機器です。選び直してください。");
                proposed[role]=value;
                if(role==2)ManualRole=3;else if(role==3)ManualRole=4;
                else if(role==4)Enter("ForwardReady");
                else{wizard.Bind((TrackerRole)role,p);if(role==0)Enter("WristRightReady");else{Mark("wrists","confirmed","実施者が手首の実物を選択して照合。");BeginVerification();}}
                Notify();return;
            }
            if(action=="retry"&&!Moving){if(Stage=="Paused")Finish(false,"再確認のため中断しました。");Start(scope);return;}
            if(action=="skip"&&Stage=="WristsChoice"){Mark("wrists","not_tested","手首は今回省略。走行用3台の機能チェックを継続。");BeginVerification();return;}
            if((action=="yes"||action=="no")&&(Stage=="VisualAnswer"||Stage=="AudioAnswer"))
            {
                Mark(Stage=="VisualAnswer"?"visual":"audio",action=="yes"?"confirmed":"attention",action=="yes"?"装着者が確認。実出力遅延は未測定。":"装着者から見え方・聞こえ方の問題あり。");
                if(Stage=="VisualAnswer"&&scope!="visual")Enter("AudioReady");else Finish(true,"");return;
            }
            if(action!="next")throw new InvalidOperationException("この段階では使えない操作です。");
            switch(Stage)
            {
                case "ForwardReady": PrepareMove("Forward");break;
                case "BackwardReady": PrepareMove("Backward");break;
                case "Candidate": ConfirmCandidate();break;
                case "WristsChoice": Enter("WristLeftReady");break;
                case "WristLeftReady":PrepareMove("WristLeft");break;
                case "WristRightReady":PrepareMove("WristRight");break;
                case "VerifyReady":PrepareMove("VerifyStill");break;
                case "VerifyForwardReady":PrepareMove("VerifyForward");break;
                case "VerifyBackwardReady":PrepareMove("VerifyBackward");break;
                case "VerifyLeftReady":PrepareMove("VerifyLeft");break;
                case "VerifyRightReady":PrepareMove("VerifyRight");break;
                case "VisualReady":Enter("Visual");break;
                case "AudioReady":Enter("Audio");break;
                case "Paused":Enter("ForwardReady");manual=true;break;
                default:throw new InvalidOperationException("画面の案内を確認してください。");
            }
        }
        void Start(string requestedScope="full")
        {
            if(Active)throw new InvalidOperationException("進行中のチェックを終了してから始めてください。");
            if(!new[]{"full","roles","axes","wrists","still","forward","backward","left","right","visual","audio","output"}.Contains(requestedScope))throw new ArgumentException("再確認する項目を選んでください。");
            if(requestedScope=="roles"||requestedScope=="axes")requestedScope="full";
            bool outputOnly=new[]{"visual","audio","output"}.Contains(requestedScope);
            if(!outputOnly&&(frame==null||frame.poses.Count(p=>p.valid)<3))throw new InvalidOperationException("車体と左右車輪の3台を追跡できる場所で始めてください。");
            if(requestedScope!="full"&&!outputOnly&&(!wizard.Calibration.Complete||wizard.Calibration.BindingError(frame)!=null))throw new InvalidOperationException("先に走行用3台の割当と校正を確認してください。");
            scope=requestedScope;
            id=Guid.NewGuid().ToString("N");sourceAtStart=sourceKind();
            log=new DeviceCheckLog(root(),id,Settings,sourceAtStart);directory=log.DirectoryPath;
            results.Clear();foreach(var item in new[]{("roles","役割の照合"),("axes","前後進と回転軸"),("wrists","手首の追加記録"),("still","静止"),("forward","前進"),("backward","後退"),("left","左旋回・左右差"),("right","右旋回・左右差"),("visual","表示の見え方"),("audio","音の聞こえ方"),("storage","チェック記録の保存")})results[item.Item1]=new CheckItem{id=item.Item1,label=item.Item2};
            foreach(var item in results.Values)if(scope!="full"&&item.id!="storage"&&item.id!=scope&&!(scope=="output"&&(item.id=="visual"||item.id=="audio"))){item.status="not_in_scope";item.detail="この再確認の対象外。以前のチェック結果は別の記録を参照。";}
            forward.Clear();backward.Clear();proposed=new string[5];Candidate=null;manual=false;Problem="";TechnicalProblem="";
            if(scope=="full"){wizard.Reset();Enter("Still");}
            else
            {
                proposed=wizard.Calibration.roles.Select(r=>r.key).ToArray();estimator=new WheelSpeedEstimator(wizard.Calibration);
                if(scope=="wrists"){wizard.Unbind(TrackerRole.LeftWrist);wizard.Unbind(TrackerRole.RightWrist);proposed[0]=proposed[1]=null;}
                Enter(scope=="visual"||scope=="output"?"VisualReady":scope=="audio"?"AudioReady":scope=="wrists"?"WristsChoice":scope=="still"?"VerifyReady":scope=="forward"?"VerifyForwardReady":scope=="backward"?"VerifyBackwardReady":scope=="left"?"VerifyLeftReady":"VerifyRightReady");
            }
        }
        void PrepareMove(string target){nextMotion=target;Enter("Countdown");}
        void Configure(string json)
        {
            if(Active)throw new InvalidOperationException("チェックを終了してから判定設定を変更してください。");
            var s=json=="default"?new PreparationSettings():JsonUtility.FromJson<PreparationSettings>(json);
            bool Range(double v,double min,double max)=>double.IsFinite(v)&&v>=min&&v<=max;
            if(s==null||!Range(s.stillSeconds,.5,10)||!Range(s.minimumTravelMeters,.2,3)||!Range(s.minimumRotationDegrees,15,180)||!Range(s.minimumAxisCoherence,.5,1)||!Range(s.minimumValidRatio,.5,1)||!Range(s.motionTimeoutSeconds,10,120)||!Range(s.stillSpeedMeters,.005,.2)||!Range(s.stillAngularDegrees,1,30)||!Range(s.maximumBodyRotationDegrees,1,45)||!Range(s.minimumWheelSeparationMeters,.1,1.5)||!Range(s.maximumAxisForwardDot,0,.7)||!Range(s.maximumReturnAxisAngleDegrees,1,60))throw new ArgumentException("判定設定の範囲を確認してください。距離・時間は正の値、比率は0.5–1です。");
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(s),Settings);wizard.Reset();results.Clear();Candidate=null;proposed=new string[5];Problem="判定設定を変更しました。割当と校正を確認してください。";Enter("Home");
        }
        void Enter(string stage)
        {
            stop();Stage=stage;entered=now;stillSince=double.NaN;segment.Clear();lastSample=double.NegativeInfinity;observed=false;motionEvidence=0;lastSpeedTime=now;speedSamples=validSpeedSamples=0;
            log?.Action(now,"stage",stage);Notify();
        }
        void Notify(){Changed?.Invoke();if(log!=null)log.Save(Snapshot(),wizard.Calibration);}
        void Mark(string key,string status,string detail){if(results.TryGetValue(key,out var item)){item.status=status;item.detail=detail;}}
        public void Tick(PoseFrame current,double time,bool presentationReady,FeedbackSettings feedback,float volume)
        {
            now=time;frame=current;if(frame==null){uiStillSince=double.NaN;previous=null;if(Active)Interrupt("位置姿勢が届きません。止まってPCで確認してください。");return;}
            bool stable=IsStill(frame,previous);previous=PreparationInference.Copy(frame);
            if(stable){if(double.IsNaN(stillSince))stillSince=time;if(double.IsNaN(uiStillSince))uiStillSince=time;}else{stillSince=double.NaN;uiStillSince=double.NaN;}
            if(!Active)return;
            try
            {
                if(sourceAtStart!=sourceKind()){Interrupt("入力方式が変わりました。チェックをやり直してください。");return;}
                log?.Pose(frame,wizard.Calibration);
                if(!presentationReady){Interrupt("ゴーグルの表示・追跡を確認してください。");return;}
                if(Stage=="Countdown"){if(time-entered>=5)Enter(nextMotion);return;}
                if(Stage=="Stopping"){if(StillLongEnough())Enter("Paused");return;}
                if(Stage=="Still"){if(StillLongEnough())Enter("ForwardReady");else if(now-entered>Settings.motionTimeoutSeconds)Interrupt("静止を確認できません。止まってPCで追跡を確認してください。");return;}
                if(Stage=="Visual"||Stage=="Audio")
                {
                    engine??=new SpeedFeedbackEngine(feedback);
                    double elapsed=time-entered;int example=(int)(elapsed/2.5);double local=elapsed%2.5;
                    if(elapsed>=7.5){Enter(Stage=="Visual"?"VisualAnswer":"AudioAnswer");return;}
                    present(engine.Update(time,example==1?0:example==0?.5:.8,example==0?0:example==1?.5:.3,true,Stage=="Visual"?StudyGroup.Visual:StudyGroup.Auditory,local<2));return;
                }
                if(IsMotion(Stage)||Stage=="WristLeft"||Stage=="WristRight")
                {
                    if(time-lastSample>=.025){segment.Add(PreparationInference.Copy(frame));lastSample=time;}
                    if(now-entered>Settings.motionTimeoutSeconds){Problem="十分な動きを確認できませんでした。安全に止まってください。";Enter("Stopping");return;}
                    if(Stage.StartsWith("Verify",StringComparison.Ordinal)){TickVerification(stable);return;}
                    if(Stage.StartsWith("Wrist",StringComparison.Ordinal)){TickWrist();return;}
                    if(!observed&&SegmentMoved())observed=true;
                    if(!observed||!StillLongEnough())return;
                    if(Stage=="Forward"){forward.Clear();forward.AddRange(segment);Enter("BackwardReady");}
                    else
                    {
                        backward.Clear();backward.AddRange(segment);
                        Candidate=PreparationInference.Identify(forward,backward,Settings,manual?proposed[2]:null,manual?proposed[3]:null,manual?proposed[4]:null);
                        if(!Candidate.ready){Problem=Candidate.reason;Enter("Paused");}
                        else{proposed[2]=Candidate.left;proposed[3]=Candidate.right;proposed[4]=Candidate.body;Problem="";Enter("Candidate");}
                    }
                }
            }
            catch(Exception e){StorageFailure(e);}
        }
        bool IsStill(PoseFrame a,PoseFrame b)
        {
            if(b==null||a.time<=b.time||a.time-b.time>.15)return false;
            var values=a.poses.Where(p=>p.valid&&b.Find(p.key)?.valid==true&&b.Find(p.key).runtimeDeviceId==p.runtimeDeviceId).Select(p=>(p.position-b.Find(p.key).position).magnitude/(a.time-b.time)<=Settings.stillSpeedMeters&&Quaternion.Angle(p.rotation,b.Find(p.key).rotation)/(a.time-b.time)<=Settings.stillAngularDegrees).ToArray();
            return values.Length>=3&&values.Count(v=>v)>values.Length/2;
        }
        bool StillLongEnough()=>!double.IsNaN(stillSince)&&now-stillSince>=Settings.stillSeconds;
        public void RequireStop(){uiStillSince=double.NaN;previous=null;}
        bool SegmentMoved()=>segment.Count>2&&segment[0].poses.Any(p=>p.valid&&segment.Last().Find(p.key)?.valid==true&&(segment.Last().Find(p.key).position-p.position).magnitude>.12f);
        void ConfirmCandidate()
        {
            if(Candidate?.ready!=true)throw new InvalidOperationException("割当候補を確認してください。");
            foreach(var key in new[]{Candidate.left,Candidate.right,Candidate.body})if(frame.Find(key)?.valid!=true)throw new InvalidOperationException("3台の追跡が戻ってから確定してください。");
            var calibration=new TrackingCalibration{bodyReady=true,createdUtc=DateTime.UtcNow.ToString("O"),left=new WheelCalibration{ready=true,axle=Candidate.leftAxis},right=new WheelCalibration{ready=true,axle=Candidate.rightAxis}};
            foreach(var role in new[]{TrackerRole.LeftWheel,TrackerRole.RightWheel,TrackerRole.Chassis}){var p=frame.Find(proposed[(int)role]);calibration.roles[(int)role]=new RoleBinding{key=p.key,runtimeDeviceId=p.runtimeDeviceId};}
            wizard.Apply(calibration);Mark("roles","confirmed","実施者が空間表示と実物を照合。");Mark("axes","confirmed","前後の回転軸を照合。独立参照による角度・速度精度は未検証。");Enter("WristsChoice");
        }
        void BeginVerification(){if(scope=="wrists"){Finish(true,"");return;}estimator=new WheelSpeedEstimator(wizard.Calibration);Enter("VerifyReady");}
        void TickWrist()
        {
            if(segment.Count<3)return;
            var body=wizard.Calibration.roles[4].key;
            var movements=frame.poses.Where(p=>p.valid&&!proposed.Contains(p.key)).Select(p=>new{p.key,distance=segment.Select(f=>{var hand=f.Find(p.key);var b=f.Find(body);return hand?.valid==true&&b?.valid==true?(Vector3?)(Quaternion.Inverse(b.rotation)*(hand.position-b.position)):null;}).Where(p=>p.HasValue).Select(p=>p.Value).ToArray()}).Where(p=>p.distance.Length>2).Select(p=>new{p.key,move=p.distance.Max(v=>(v-p.distance[0]).magnitude)}).OrderByDescending(p=>p.move).ToArray();
            if(movements.Length==0||movements[0].move<.15f)return;
            observed=true;if(!StillLongEnough())return;
            int role=Stage=="WristLeft"?0:1;
            if(movements.Length>1&&movements[1].move>.06f){ManualRole=role;manualNext="wrist";Problem="手首を実物から選んでください。";Enter("Manual");return;}
            var pose=frame.Find(movements[0].key);proposed[role]=pose.key;wizard.Bind((TrackerRole)role,pose);
            if(role==0)Enter("WristRightReady");else{Mark("wrists","confirmed","左右の手首動作から識別。元データ記録専用。");BeginVerification();}
        }
        void TickVerification(bool stable)
        {
            if(!wizard.Calibration.Complete){Interrupt("校正が無効になりました。割当と校正をやり直してください。");return;}
            speed=estimator.Update(frame);
            log?.Speed(now,Stage,speed);speedSamples++;if(speed.valid)validSpeedSamples++;
            double interval=Math.Min(.1,Math.Max(0,now-lastSpeedTime));lastSpeedTime=now;
            if(!speed.valid)return;
            if(Stage=="VerifyStill")
            {
                if(StillLongEnough()){Mark("still",Math.Abs(speed.leftRps)<.05&&Math.Abs(speed.rightRps)<.05?"confirmed":"attention","静止中の推定速度を確認。");if(scope=="still")Finish(true,"");else Enter("VerifyForwardReady");}return;
            }
            bool match=Stage=="VerifyForward"?speed.leftRps>.03&&speed.rightRps>.03:Stage=="VerifyBackward"?speed.leftRps<-.03&&speed.rightRps<-.03:Stage=="VerifyLeft"?speed.rightRps-speed.leftRps>.05:speed.leftRps-speed.rightRps>.05;
            if(match)motionEvidence+=interval;
            if(motionEvidence>=.4)observed=true;
            if(!observed||!StillLongEnough())return;
            string key=Stage=="VerifyForward"?"forward":Stage=="VerifyBackward"?"backward":Stage=="VerifyLeft"?"left":"right";
            double ratio=validSpeedSamples/(double)Math.Max(1,speedSamples);
            Mark(key,ratio>=Settings.minimumValidRatio?"confirmed":"attention","方向・左右差を検出。有効率を保存。速度の独立参照精度は未検証。");results[key].validRatio=ratio;results[key].evidenceSeconds=motionEvidence;
            if(scope==key){Finish(true,"");return;}
            Enter(Stage=="VerifyForward"?"VerifyBackwardReady":Stage=="VerifyBackward"?"VerifyLeftReady":Stage=="VerifyLeft"?"VerifyRightReady":"VisualReady");
        }
        public void Interrupt(string reason)
        {if(!Active){stop();return;}stop();if(Stage=="Stopping"||Stage=="Paused")return;TechnicalProblem=reason;Problem=reason.Contains("_")?"機器の状態が変わりました。止まってPCで確認してください。":reason;Enter("Stopping");}
        void Finish(bool completed,string reason)
        {
            stop();Problem=reason;Stage="Result";Mark("storage","confirmed","ゴーグルに保存。PCの回収・照合は別表示。");
            try{log?.Action(now,"closed",completed?"completed":"interrupted");log?.Dispose();log?.Save(Snapshot(),wizard.Calibration);if(log!=null)File.WriteAllText(Path.Combine(directory,"closed.json"),"{\"files_closed\":true,\"check_complete\":"+(completed?"true":"false")+"}");log=null;Changed?.Invoke();if(!string.IsNullOrEmpty(directory))Closed?.Invoke(directory);}
            catch(Exception e){StorageFailure(e);}
        }
        void StorageFailure(Exception e)
        {stop();TechnicalProblem=e.ToString();Problem="入力・保存を確認できません。PCで確認してください。";Stage="Result";Mark("storage","attention","保存を確認できません。元記録は保持。");try{log?.Dispose();log?.Save(Snapshot(),wizard.Calibration);}catch{}log=null;Changed?.Invoke();}
        public PreparationSnapshot Snapshot()
        {
            var content=Content();
            return new PreparationSnapshot{checkId=id,stage=Stage,scope=scope,title=content.title,instruction=content.instruction,detail=content.detail,problem=Problem,technicalProblem=TechnicalProblem,directory=directory,settings=Settings,
                active=Active,moving=Moving,showLabels=ShowLabels,safeToShowPanel=!double.IsNaN(uiStillSince)&&now-uiStillSince>=Settings.stillSeconds,manualRole=Stage=="Manual"?ManualRole:-1,proposedBindings=(string[])proposed.Clone(),actions=content.actions,
                requiredTracked=new[]{2,3,4}.Count(i=>frame?.Find(proposed[i]??wizard.Calibration.roles[i].key)?.valid==true),wristTracked=new[]{0,1}.Count(i=>frame?.Find(wizard.Calibration.roles[i].key)?.valid==true),
                results=results.Values.ToArray(),step=Stage=="Home"?0:Stage=="Still"||Stage.Contains("Forward")||Stage=="Backward"||Stage=="BackwardReady"?1:Stage=="Candidate"||Stage=="Manual"?2:Stage.StartsWith("Wrist")?3:Stage.StartsWith("Verify")?4:Stage.StartsWith("Visual")||Stage.StartsWith("Audio")?5:6,
                leftRps=speed.leftRps,rightRps=speed.rightRps,speedValid=speed.valid};
        }
        (string title,string instruction,string detail,PreparationAction[] actions) Content()
        {
            PreparationAction A(string action,string label)=>new PreparationAction(action,label);
            var next=new[]{A("next","準備できた・始める")};
            switch(Stage)
            {
                case "Home":return("準備とチェック","車いすの動きを確かめます","実施者用。広い平地で確認します。",new[]{A("start","チェックを始める"),A("wait","実験を待つ")});
                case "Waiting":return("実験を待っています","実施者の案内をお待ちください","準備の変更は停止中に行います。",new[]{A("start","準備を確認する")});
                case "Still":return("まず、止まります","車いすを止めてください","動きが落ち着くと次へ進みます。",Array.Empty<PreparationAction>());
                case "Countdown":return("動く準備","コントローラーを置いてください",Math.Max(0,5-(int)(now-entered))+"秒後に案内を始めます。",Array.Empty<PreparationAction>());
                case "ForwardReady":return("前へ進む準備","前に1 mほど、ゆっくり進んで止まります","周囲を確認してから始めてください。",next);
                case "BackwardReady":return("後ろへ戻る準備","後ろを確認し、元の場所へゆっくり戻ります","安全に止まると自動で確認します。",next);
                case "Forward":return("前へ進む","前へゆっくり進んで、止まってください","1 mほど。無理な距離・速度は不要です。",Array.Empty<PreparationAction>());
                case "Backward":return("後ろへ戻る","後ろへゆっくり戻って、止まってください","後方を確認してください。",Array.Empty<PreparationAction>());
                case "Candidate":return("割当の確認","目印が実物と合っていますか？","左車輪・右車輪・車体を確認してください。",new[]{A("next","合っている・確定"),A("manual","実物を選び直す")});
                case "Manual":return("実物から選ぶ",new[]{"左手首","右手首","左車輪","右車輪","車体"}[ManualRole]+"のトラッカーを選んでください","目印を指してトリガーで選択。PCからも選べます。",Array.Empty<PreparationAction>());
                case "WristsChoice":return("手首の追加確認","手首のトラッカーも使いますか？","手首は動きの記録用。走行FBは3台で確認できます。",new[]{A("next","手首も確認する"),A("skip","今回は省略する")});
                case "WristLeftReady":case "WristRightReady":return("手首の確認",Stage=="WristLeftReady"?"左手を上げ下げして止めます":"右手を上げ下げして止めます","反対の手と車いすは止めたままにします。",next);
                case "WristLeft":case "WristRight":return("手首の確認",Stage=="WristLeft"?"左手を軽く上げ下げしてください":"右手を軽く上げ下げしてください","動かしたら止めてお待ちください。",Array.Empty<PreparationAction>());
                case "VerifyReady":return("動作チェック","停止・前後進・左右の曲がりを確認します","校正の確認です。測定精度の保証とは別です。",next);
                case "VerifyStill":return("停止の確認","車いすを止めてお待ちください","停止時の値を確認しています。",Array.Empty<PreparationAction>());
                case "VerifyForwardReady":case "VerifyBackwardReady":case "VerifyLeftReady":case "VerifyRightReady":
                    return("動作の確認",Stage=="VerifyForwardReady"?"ゆっくり前へ進んで止まります":Stage=="VerifyBackwardReady"?"ゆっくり後ろへ戻って止まります":Stage=="VerifyLeftReady"?"ゆっくり左へ曲がって止まります":"ゆっくり右へ曲がって止まります","動く前に周囲を確認してください。",next);
                case "VerifyForward":case "VerifyBackward":case "VerifyLeft":case "VerifyRight":return("動作を確認中",Stage=="VerifyForward"?"前へ進んで、止まってください":Stage=="VerifyBackward"?"後ろへ戻って、止まってください":Stage=="VerifyLeft"?"左へ曲がって、止まってください":"右へ曲がって、止まってください","ゆっくりで大丈夫です。",Array.Empty<PreparationAction>());
                case "VisualReady":case "AudioReady":return("見え方・聞こえ方",Stage=="VisualReady"?"左右の棒の見本を表示します":"左・右・左右差の音の見本を流します","停止したまま確認してください。",next);
                case "Visual":case "Audio":return("見本を確認中",Stage=="Visual"?"左・右・左右差の棒を確認してください":"左・右・左右差の音を確認してください","見本の値です。実際の速度とは別です。",Array.Empty<PreparationAction>());
                case "VisualAnswer":case "AudioAnswer":return("確認できましたか？",Stage=="VisualAnswer"?"左右の棒と長さの違いが見えましたか？":"左右の音と高さの違いが分かりましたか？","問題があればPCで詳しく確認します。",new[]{A("yes","確認できた"),A("no","確認しにくい")});
                case "Stopping":return("いったん停止","安全に止まってください",Problem,Array.Empty<PreparationAction>());
                case "Paused":return("確認をやり直します",Problem,"動きや追跡を確認するか、実物を選んでください。",new[]{A("retry","最初からやり直す"),A("manual","実物を選ぶ")});
                default:
                    int confirmed=results.Values.Count(x=>x.status=="confirmed"),attention=results.Values.Count(x=>x.status=="attention"),pending=results.Values.Count(x=>x.status=="pending"||x.status=="not_tested");
                    string issue=results.Values.FirstOrDefault(x=>x.status=="attention")?.label;
                    return("チェック結果",$"確認済み {confirmed}項目\n要確認 {attention}・未実施 {pending}",issue!=null?issue+"をPCで確認してください。":"測定精度の検証は別に行います。",new[]{A("wait","実験待機へ"),A("retry","もう一度確認する")});
            }
        }
        public void Dispose(){stop();if(log!=null){try{Finish(false,"アプリ終了。チェックは未完了です。");}finally{log?.Dispose();}}}
#if UNITY_EDITOR
        public static PreparationSnapshot Preview(string stage)
        {
            var workflow=new PreparationWorkflow(new CalibrationWizard(),()=>"",()=>"synthetic_preview",_=>{},()=>{});
            workflow.Stage=stage;workflow.uiStillSince=-3;workflow.id="preview-only";workflow.proposed=new[]{"synthetic-0","synthetic-1","synthetic-2","synthetic-3","synthetic-4"};
            foreach(var item in new[]{("roles","役割の照合"),("axes","前後進と回転軸"),("wrists","手首の追加記録"),("still","静止"),("forward","前進"),("backward","後退"),("left","左旋回・左右差"),("right","右旋回・左右差"),("visual","表示の見え方"),("audio","音の聞こえ方"),("storage","チェック記録の保存")})
                workflow.results[item.Item1]=new CheckItem{id=item.Item1,label=item.Item2,status=stage=="Result"?"confirmed":"pending",detail="画面確認用の合成状態。実機の結果ではありません。"};
            if(stage=="Paused")workflow.Problem="候補を一つに絞れませんでした。";
            return workflow.Snapshot();
        }
#endif
    }
}
