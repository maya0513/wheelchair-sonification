using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    // Rendering and input only. The workflow owns instructions, decisions, and the available actions.
    public sealed class PreparationView : IDisposable
    {
        readonly FocusVisionRig rig;
        readonly ResearchVisitHost host;
        readonly RectTransform panel,footer;
        readonly TMP_Text title,instruction,detail,footerText,connection;
        readonly List<Button> buttons=new List<Button>();
        readonly Button cancel;
        readonly Dictionary<string,RectTransform> markers=new Dictionary<string,RectTransform>();
        string contentKey="",localProblem=""; bool menuOpen=true,wasPanelVisible,awaitingStop;
        public TMP_Text Title => title;
        public bool PanelVisible => panel.gameObject.activeSelf;
        public bool LabelsVisible {get;private set;}
        public RectTransform Panel => panel;
        public RectTransform Footer => footer;
        public PreparationView(FocusVisionRig rig,ResearchVisitHost host,Transform parent)
        {
            this.rig=rig;this.host=host;
            panel=XrUi.Canvas("Preparation panel",parent,new Vector2(1000,650));
            rig.WorldCanvas(panel,parent,Vector3.zero,new Vector2(1000,650),2*1.4f*Mathf.Tan(18*Mathf.Deg2Rad)/1000);
            XrUi.Fill(panel,XrUi.Paper);title=XrUi.Text(panel,"Heading","準備とチェック",40,12,920,95,56);
            instruction=XrUi.Text(panel,"Instruction","",40,108,920,150,48);
            detail=XrUi.Text(panel,"Detail","",40,260,920,110,36);
            connection=XrUi.Text(panel,"Connection","",40,580,650,54,34);
            for(int i=0;i<2;i++){int slot=i;var b=Button(panel,"Action "+i,40,376+i*104,920,96);b.onClick.AddListener(()=>RunAction(slot));buttons.Add(b);}
            cancel=Button(panel,"Cancel",760,582,200,54);cancel.GetComponentInChildren<TMP_Text>().fontSize=36;cancel.GetComponentInChildren<TMP_Text>().text="中断";
            cancel.onClick.AddListener(()=>Send("cancel"));
            footer=XrUi.Canvas("Movement instruction",rig.Camera.transform,new Vector2(1100,175));
            footer.localPosition=new Vector3(0,-Mathf.Tan(16*Mathf.Deg2Rad)*1.4f,1.4f);footer.localScale=Vector3.one*.00065f;
            XrUi.Fill(footer,XrUi.Paper);footerText=XrUi.Text(footer,"Short instruction","",24,10,1052,155,48);
            Recenter();
        }
        static Button Button(RectTransform parent,string name,float x,float y,float width,float height)
        {
            var rect=XrUi.Rect(name,parent,x,y,width,height);var image=XrUi.Fill(rect,XrUi.Blue);image.raycastTarget=true;
            var b=rect.gameObject.AddComponent<Button>();b.targetGraphic=image;var colors=b.colors;colors.highlightedColor=new Color(.82f,.9f,1);colors.pressedColor=new Color(.6f,.75f,1);b.colors=colors;
            var label=XrUi.Text(rect,"Label","",24,0,width-48,height,48);label.color=Color.white;label.alignment=TextAlignmentOptions.Center;
            return b;
        }
        void RunAction(int index){var actions=host.Preparation.View.actions;if(index<actions.Length)Send(actions[index].id);}
        void Send(string action,string value="",int role=0)
        {var result=host.ExecuteLocal("prep."+action,value,role);localProblem=result.ok?"":result.message.Length<40?result.message:"操作できません。PCで理由を確認してください。";menuOpen=true;}
        public void Recenter()
        {
            var forward=rig.Camera.transform.forward.normalized;
            panel.position=rig.Camera.transform.position+forward*1.4f+Vector3.down*.08f;panel.rotation=Quaternion.LookRotation(forward,Vector3.up);
        }
        public void Toggle()
        {
            if(host.Research.Active){host.Suspend("hmd_menu_requested");menuOpen=true;return;}
            if(host.Preparation.Moving){host.Preparation.Interrupt("操作メニューを開きました。安全に止まってください。");menuOpen=true;return;}
            if(host.Preparation.Stage=="Waiting"){Send("home");menuOpen=true;Recenter();return;}
            menuOpen=!menuOpen;if(menuOpen)Recenter();
        }
        public void Render(bool paired,string code,bool ready,bool connectionMismatch=false)
        {
            RenderState(host.Preparation.View,paired,code,ready,host.Research.Active);
            if(!paired&&connectionMismatch)connection.text="PC側も同じ版へ更新してください";
        }
        void RenderState(PreparationSnapshot state,bool paired,string code,bool ready,bool active)
        {
            bool example=state.stage=="Visual"||state.stage=="Audio";
            bool waiting=state.stage=="Waiting";
            if(active||state.moving)awaitingStop=true;
            if(!active&&!state.moving&&state.safeToShowPanel)awaitingStop=false;
            bool showPanel=ready&&!active&&!state.moving&&!example&&menuOpen&&!waiting&&!awaitingStop;
            cancel.gameObject.SetActive(state.active);
            panel.gameObject.SetActive(showPanel);
            if(showPanel&&!wasPanelVisible)Recenter();wasPanelVisible=showPanel;
            footer.gameObject.SetActive(!active&&!example&&(state.moving||waiting||!ready||awaitingStop));
            footerText.text=!ready?"停止してお待ちください":waiting?"実施者の案内をお待ちください":state.stage=="Countdown"?state.instruction+"\n"+state.detail:state.instruction;
            if(!active&&waiting&&!paired)footerText.text="PC接続の確認番号\n"+code;
            if(awaitingStop&&!state.moving&&!active)footerText.text="安全に止まってお待ちください";
            string key=state.stage+"|"+state.instruction+"|"+state.detail+"|"+state.problem+"|"+localProblem;
            if(contentKey!=key)
            {
                contentKey=key;title.text=state.title;instruction.text=state.instruction;
                detail.text=!string.IsNullOrEmpty(localProblem)?localProblem:!string.IsNullOrEmpty(state.problem)&&state.stage!="Paused"?state.problem:state.detail;
                for(int i=0;i<buttons.Count;i++){buttons[i].gameObject.SetActive(i<state.actions.Length);if(i<state.actions.Length){buttons[i].GetComponentInChildren<TMP_Text>().text=state.actions[i].label;buttons[i].targetGraphic.color=i==0?XrUi.Blue:new Color(.9f,.93f,.97f);buttons[i].GetComponentInChildren<TMP_Text>().color=i==0?Color.white:XrUi.Ink;}}
            }
            connection.text=paired?"PCに接続済み":"PC番号 "+code;
            int tracked=host.Research.Frame?.poses.Count(p=>p.valid)??0;
            bool few=tracked<3&&(state.stage=="Home"||state.stage=="Result");
            for(int i=0;i<buttons.Count&&i<state.actions.Length;i++)buttons[i].interactable=!few||(state.actions[i].id!="start"&&state.actions[i].id!="retry");
            if(few)detail.text="PCで3台の接続・追跡を確認してください。";
            LabelsVisible=!active&&state.showLabels&&!state.moving&&menuOpen&&!awaitingStop;
            RenderMarkers(state,LabelsVisible);
            rig.UpdatePointers(showPanel||LabelsVisible);
        }
        void RenderMarkers(PreparationSnapshot state,bool visible)
        {
            foreach(var old in markers.Values)old.gameObject.SetActive(false);
            if(!visible||host.Research.Frame==null)return;
            foreach(var p in host.Research.Frame.poses.Where(p=>p.valid))
            {
                if(!markers.TryGetValue(p.key,out var marker))
                {
                    marker=XrUi.Canvas("Tracker marker",rig.TrackingOrigin,new Vector2(260,110));
                    rig.WorldCanvas(marker,rig.TrackingOrigin,Vector3.zero,new Vector2(260,110),.0007f);
                    var b=Button(marker,"Select tracker",0,0,260,110);b.GetComponentInChildren<TMP_Text>().fontSize=48;
                    string key=p.key;b.onClick.AddListener(()=>{if(host.Preparation.Stage=="Manual")Send("select",key,host.Preparation.ManualRole);});markers.Add(p.key,marker);
                }
                marker.gameObject.SetActive(true);
                var position=rig.TrackingOrigin.TransformPoint(p.position);var to=position-rig.Camera.transform.position;
                // Keep readable labels at least 1 m away, along the actual object's line of sight.
                marker.position=rig.Camera.transform.position+to.normalized*Mathf.Max(1,to.magnitude);
                marker.rotation=Quaternion.LookRotation(marker.position-rig.Camera.transform.position,Vector3.up);
                marker.localScale=Vector3.one*Mathf.Max(1,to.magnitude)*.0007f;
                int role=Array.IndexOf(state.proposedBindings,p.key);
                int index=Array.IndexOf(host.Research.Frame.poses.Select(x=>x.key).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),p.key);
                string label=role>=0?new[]{"左手首","右手首","左車輪","右車輪","車体"}[role]:"機器 "+(char)('A'+index);
                marker.GetComponentInChildren<TMP_Text>().text=label;
            }
        }
        public void HideForPresentation(){panel.gameObject.SetActive(false);footer.gameObject.SetActive(false);foreach(var marker in markers.Values)marker.gameObject.SetActive(false);rig.UpdatePointers(false);wasPanelVisible=false;}
        public void ShowPreview(PreparationSnapshot state)
        {menuOpen=true;RenderState(state,false,"123456",true,false);}
        public void Dispose(){UnityEngine.Object.Destroy(panel.gameObject);UnityEngine.Object.Destroy(footer.gameObject);foreach(var marker in markers.Values)UnityEngine.Object.Destroy(marker.gameObject);}
    }
}
