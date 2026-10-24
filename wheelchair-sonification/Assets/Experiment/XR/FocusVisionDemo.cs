using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using VIVE.OpenXR;
using VIVE.OpenXR.Passthrough;
using TMPro;
using LayerType = VIVE.OpenXR.CompositionLayer.LayerType;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
namespace WheelchairSonification.XR
{
    [DefaultExecutionOrder(100), RequireComponent(typeof(DemoController))]
    public sealed class FocusVisionDemo : MonoBehaviour
    {
        public FocusVisionRig Rig { get; private set; }
        public XrGauge Gauge { get; private set; }
        public ResearchController Research { get; private set; }
        public ResearchVisitHost VisitHost { get; private set; }
        public OperatorLink Link { get; private set; }
        public TMP_Text WaitingLabel { get; private set; }
        public bool IsEditorPreview { get; private set; }
        public bool Ready => ready;
        UnityFeedbackOutput output; UltimatePoseSource actualInput;
        XRManagerSettings manager; VivePassthrough feature; XrPassthroughHTC passthrough;
        bool hasPassthrough, nativeStarted, paused, ready, attempted, wasTracking, focused=true;
        double nextAttempt, exampleUntil; string runtimeStatus="OpenXR準備中";
        SpeedFeedbackEngine exampleEngine; StudyGroup exampleGroup; double exampleLeft,exampleRight;
        void Awake()
        {
            output=GetComponent<DemoController>().Output;
            Rig=new FocusVisionRig(transform); Gauge=XrGauge.InstantiateFor(Rig.Camera); output.BindGauge(Gauge);
            var waiting=XrUi.Canvas("Participant waiting",Rig.Camera.transform,new Vector2(850,230));
            waiting.localPosition=new Vector3(0,0,1.4f); waiting.localScale=Vector3.one*.001f; XrUi.Fill(waiting,XrUi.Paper);
            WaitingLabel=XrUi.Text(waiting,"Waiting text","準備中",24,20,802,190,56);
            Research=gameObject.AddComponent<ResearchController>(); Research.Output=output;
            actualInput=new UltimatePoseSource(); Research.SetSource(new BorrowedPoseSource(actualInput));
            VisitHost=gameObject.AddComponent<ResearchVisitHost>(); VisitHost.Initialize(Research);
            VisitHost.SourceFactory=_=>new BorrowedPoseSource(actualInput); VisitHost.Present=PresentExample; VisitHost.StopPresentation=StopExample;
            Link=gameObject.AddComponent<OperatorLink>(); Link.Host=VisitHost;
        }
        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Link.Begin(); manager=XRGeneralSettings.Instance?.Manager;
            if(manager==null){SetReady(false,"Android XR設定なし");yield break;}
            yield return manager.InitializeLoader();
            if(manager.activeLoader==null){SetReady(false,"OpenXR起動失敗");yield break;}
            manager.StartSubsystems();nativeStarted=true;feature=OpenXRSettings.Instance.GetFeature<VivePassthrough>();
            var input=manager.activeLoader.GetLoadedSubsystem<XRInputSubsystem>(); if(input!=null)input.trackingOriginUpdated+=OnTrackingOriginUpdated;
#else
            yield break;
#endif
        }
        void Update()
        {
            Rig.UpdatePointers(false); // Participant never needs a controller.
            if(nativeStarted&&!paused&&focused)UpdateRuntime();
            bool example=exampleEngine!=null&&Time.realtimeSinceStartupAsDouble<exampleUntil;
            if(example) output.Apply(exampleEngine.Update(Time.realtimeSinceStartupAsDouble,exampleLeft,exampleRight,true,exampleGroup,true));
            else if(exampleEngine!=null)StopExample();
            WaitingLabel.transform.parent.gameObject.SetActive(!Research.Active&&!example);
            if(!IsEditorPreview) WaitingLabel.text=!Link.Paired?"接続確認\n"+(Link.PairingCode??"PCで準備"):!ready?"停止してお待ちください":"準備中";
        }
        public void PresentExample(StudyGroup group,double left,double right)
        {
            if(!ready||Research.Active)return;
            exampleGroup=group;exampleLeft=left;exampleRight=right;exampleEngine=new SpeedFeedbackEngine(Research.Settings);
            output.Volume=Research.Volume;exampleUntil=Time.realtimeSinceStartupAsDouble+2;
        }
        public void StopExample(){exampleEngine=null;if(!Research.Active)output.Stop();}
        public void Recenter(){Gauge.transform.localPosition=new Vector3(0,-Mathf.Tan(10*Mathf.Deg2Rad)*1.4f,1.4f);Gauge.transform.localRotation=Quaternion.identity;}
        public void TogglePanel(){} // Editor simulator key retained; there is no participant menu.
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
            Research.PresentationReady = value;
            if (lost)
            {
                VisitHost.Suspend("xr_or_head_tracking_lost");
                Research.InvalidateCalibration("xr_or_head_tracking_lost");
                StopExample();
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem _)
        { VisitHost.Suspend("calibration_invalid"); Research.InvalidateCalibration("tracking_origin_changed_recalibrate"); }


#if UNITY_EDITOR
        public void EnableEditorPreview(bool technician=false)
        {IsEditorPreview=true;Rig.Camera.stereoTargetEye=StereoTargetEyeMask.None;Research.SetSource(new SyntheticPoseSource());SetReady(true,"Editor synthetic");}
        public void ShowParticipantPreview(string text,bool visible){WaitingLabel.text=text;WaitingLabel.transform.parent.gameObject.SetActive(visible);}
#endif
        void ReleasePassthrough()
        {
            if(hasPassthrough&&feature!=null&&feature.XrSessionCreated&&!feature.XrSessionEnding)PassthroughAPI.DestroyPassthrough(passthrough);
            hasPassthrough=false;attempted=false;
        }
        void OnApplicationPause(bool pause)
        {paused=pause;if(Research==null)return;if(pause){VisitHost.Suspend("application_pause");SetReady(false,"アプリ中断");ReleasePassthrough();}else nextAttempt=Time.realtimeSinceStartupAsDouble+.2;}
        void OnApplicationFocus(bool focus)
        {
#if UNITY_EDITOR
            if(IsEditorPreview)return;
#endif
            focused=focus;if(!focus&&Research!=null){VisitHost.Suspend("application_focus_lost");SetReady(false,"フォーカス喪失");}
        }
        void OnDestroy()
        {
            Research?.Interrupt("xr_scene_closed");actualInput?.Dispose();ReleasePassthrough();
            if(nativeStarted&&manager!=null){var input=manager.activeLoader?.GetLoadedSubsystem<XRInputSubsystem>();if(input!=null)input.trackingOriginUpdated-=OnTrackingOriginUpdated;manager.StopSubsystems();manager.DeinitializeLoader();}
            Rig?.Dispose();
        }
    }
}
