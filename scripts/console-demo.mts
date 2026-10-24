import { createHash, randomUUID } from 'node:crypto';
export const demoDeviceId = 'de000000000000000000000000000004';
export const demoCode = '123456';
export function demoSnapshot(overrides:any={}) {
  return {schema:2,revision:0,visitId:'',participantId:'',trialId:'',group:'Auditory',sourceKind:'synthetic_pose_fixture',phase:'Baseline',task:'FigureEight',repetition:1,status:'停止中',running:false,calibrationReady:true,presentationReady:true,trainingEnded:false,transferring:false,calibrationInstruction:'模擬校正。実測の性能を示しません。',exportStatus:'未回収',error:'',devices:Array.from({length:5},(_,i)=>({key:'synthetic-'+i,product:'合成機器',runtimeDeviceId:900+i,valid:true,x:i*.2,y:.5,z:0,rx:i*12,ry:0,rz:0})),bindings:Array.from({length:5},(_,i)=>'synthetic-'+i),...overrides};
}
export async function startDemoDevice(endpoint:string,options:{snapshot?:any;autoPair?:boolean}={}) {
  let token='',running=true,state=options.snapshot||demoSnapshot(),result:any=null,lastId='';
  const request=async(path:string,body:any)=>{const response=await fetch(endpoint+path,{method:'POST',headers:{'Content-Type':'application/json',...(token?{'X-Device-Token':token}:{})},body:JSON.stringify(body)});return response.json();};
  console.log('画面デモの接続確認番号: '+demoCode);
  const loop=async()=>{while(running){try{
    if(!token){const hello=await request('/device/hello',{schema:2,deviceId:demoDeviceId,codeHash:createHash('sha256').update(demoDeviceId+':'+demoCode).digest('hex')});token=hello.pairToken||'';}
    else {const reply=await request('/device/exchange',{state,result});if(reply.command&&reply.command.id!==lastId){const c=reply.command;lastId=c.id;result={id:c.id,ok:true,message:'模擬モードで反映しました。'};
      if(c.action==='createVisit')state=demoSnapshot({visitId:randomUUID(),participantId:c.value,revision:state.revision});
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
