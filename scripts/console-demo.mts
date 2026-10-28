import { createHash, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
const preparationFixtures=JSON.parse(readFileSync(new URL('./console/preparation-fixtures.json',import.meta.url),'utf8'));
export function preparationFixture(stage='Home') { return structuredClone(preparationFixtures.stages.find((s:any)=>s.stage===stage)||preparationFixtures.stages[0]); }
export const demoDeviceId = 'de000000000000000000000000000004';
export const demoCode = '123456';
export function demoSnapshot(overrides:any={}) {
  return {schema:3,revision:0,visitId:'',participantId:'',trialId:'',group:'Auditory',sourceKind:'synthetic_pose_fixture',phase:'Baseline',task:'FigureEight',repetition:1,status:'停止中',running:false,calibrationReady:true,presentationReady:true,trainingEnded:false,transferring:false,calibrationInstruction:'模擬校正。実測の性能を示しません。',exportStatus:'未回収',error:'',preparation:preparationFixture(),devices:Array.from({length:5},(_,i)=>({key:'synthetic-'+i,product:'合成機器',runtimeDeviceId:900+i,valid:true,x:i*.2,y:.5,z:0,rx:i*12,ry:0,rz:0})),bindings:Array.from({length:5},(_,i)=>'synthetic-'+i),...overrides};
}
export async function startDemoDevice(endpoint:string,options:{snapshot?:any;autoPair?:boolean}={}) {
  let token='',running=true,state=options.snapshot||demoSnapshot(),result:any=null,lastId='',advanceAt=0,advanceTo='';
  const show=(stage:string)=>{state={...state,preparation:preparationFixture(stage),revision:state.revision+1};};
  const advance=(stage:string)=>{advanceAt=performance.now()+900;advanceTo=stage;};
  const request=async(path:string,body:any)=>{const response=await fetch(endpoint+path,{method:'POST',headers:{'Content-Type':'application/json',...(token?{'X-Device-Token':token}:{})},body:JSON.stringify(body)});return response.json();};
  console.log('画面デモの接続確認番号: '+demoCode);
  const loop=async()=>{while(running){try{
    if(advanceAt&&performance.now()>=advanceAt){advanceAt=0;show(advanceTo);}
    if(!token){const hello=await request('/device/hello',{schema:3,deviceId:demoDeviceId,codeHash:createHash('sha256').update(demoDeviceId+':'+demoCode).digest('hex')});token=hello.pairToken||'';}
    else {const reply=await request('/device/exchange',{state,result});if(reply.command&&reply.command.id!==lastId){const c=reply.command;lastId=c.id;result={id:c.id,ok:true,message:'模擬モードで反映しました。'};
      if(c.action.startsWith('prep.')) {
        const action=c.action.slice(5),stage=state.preparation?.stage;
        if(action==='start'||action==='retry'){show('Still');advance('ForwardReady');}
        else if(action==='cancel'){advanceAt=0;show('Result');state.preparation.problem='画面デモでチェックを中断しました。';}
        else if(action==='wait')show('Waiting');
        else if(action==='home')show('Home');
        else if(action==='recheck')show(c.value==='audio'?'AudioAnswer':c.value==='visual'||c.value==='output'?'VisualAnswer':c.value==='wrists'?'WristsChoice':'ForwardReady');
        else if(action==='manual')show('Manual');
        else if(action==='select'){const p=state.preparation;p.proposedBindings[c.number]=c.value;if(c.number===2)p.manualRole=3;else if(c.number===3)p.manualRole=4;else show('ForwardReady');}
        else if(action==='skip'){show('VerifyReady');}
        else if(action==='yes'||action==='no')show(stage==='VisualAnswer'?'AudioAnswer':'Result');
        else if(action==='next'){
          if(stage==='ForwardReady'){show('Forward');advance('BackwardReady');}
          else if(stage==='BackwardReady'){show('Backward');advance('Candidate');}
          else if(stage==='Candidate')show('WristsChoice');
          else if(stage==='WristsChoice'){show('VerifyReady');}
          else if(stage==='VerifyReady'){show('VerifyLeft');advance('VisualAnswer');}
          else show('Result');
        }
      }
      else if(c.action==='createVisit')state=demoSnapshot({visitId:randomUUID(),participantId:c.value,revision:state.revision});
      else if(c.action==='start')state={...state,running:true,status:'模擬記録中',trialId:randomUUID()};
      else if(c.action==='finishTrial'||c.action==='abort')state={...state,running:false,status:c.action==='abort'?'未完了':'記録終了'};
      else if(c.action==='trainingEnd')state={...state,trainingEnded:true};
      else if(c.action==='setSelection'){const s=JSON.parse(c.value);state={...state,phase:['Preparation','Baseline','Training','ImmediateAssessment','Rest','RetentionAssessment'][s.phase],task:['None','Straight','FigureEight'][s.task],repetition:s.repetition};}
      else if(c.action==='setGroup')state={...state,group:c.value};
      else if(c.action==='setInput')state={...state,sourceKind:c.value==='synthetic'?'synthetic_pose_fixture':'actual_unavailable_in_demo'};
      state={...state,revision:state.revision+1};
    }}
  }catch{}await new Promise(a=>setTimeout(a,150));}};
  void loop();return Object.assign(()=>{running=false;},{setSnapshot:(value:any)=>{state=value;}});
}
