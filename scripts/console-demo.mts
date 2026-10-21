import { createHash } from 'node:crypto';
export const demoDeviceId = 'de000000000000000000000000000003';
export const demoCode = '123456';
export function demoSnapshot(overrides: any = {}) {
  return { schema:1, revision:0, visitId:'', participantId:'', title:'実施前設定で来訪を作成', instruction:'画面の操作を試すための合成データです。実機や研究用の性能を示すものではありません。',
    kind:'setup',status:'setup',group:'unallocated',sourceKind:'synthetic_pose_fixture',primaryLabel:'確認して次へ',tracked:5,calibrated:true,saveHealthy:true,presentationReady:true,
    running:false,canStart:false,canRetry:false,canSkip:false,elapsed:0,remaining:0,countdownRemaining:0,trialElapsed:0,quizAwaiting:false,quizModality:'Auditory',quizQuestion:1,quizAttempt:1,
    calibrationInstruction:'模擬校正。実車の校正結果ではありません。',recordCounts:'画面確認用データのみ。実測記録なし。',lastReason:'',targetDegrees:70,
    devices:Array.from({length:5},(_,i)=>({key:'synthetic-'+i,valid:true,speed:i===0?.08:0})),bindings:Array.from({length:5},(_,i)=>'synthetic-'+i),...overrides };
}
export async function startDemoDevice(endpoint: string, options: {snapshot?: any; autoPair?: boolean} = {}) {
  let token='', running=true, state=options.snapshot || demoSnapshot(), result:any=null;
  const request=async(path:string,body:any)=>{const response=await fetch(endpoint+path,{method:'POST',headers:{'Content-Type':'application/json',...(token?{'X-Device-Token':token}:{})},body:JSON.stringify(body)});return await response.json();};
  console.log('画面デモの接続確認番号: '+demoCode);
  const loop=async()=>{
    while(running){try{
      if(!token){const hello=await request('/device/hello',{deviceId:demoDeviceId,codeHash:createHash('sha256').update(demoDeviceId+':'+demoCode).digest('hex')});token=hello.pairToken||'';}
      else {const reply=await request('/device/exchange',{state,result});
        if(reply.command){const c=reply.command;result={id:c.id,ok:true,message:'画面デモで受信しました。実測は行いません。'};
          if(c.action==='createVisit')state=demoSnapshot({visitId:'demo-visit',participantId:'P'+String(c.number).padStart(3,'0'),title:'説明・同意・適格性',kind:'manual',status:'ready',canStart:true,revision:state.revision+1});
          else if(c.action==='abort')state={...state,status:'interrupted',lastReason:c.value,running:false,canStart:false,canSkip:true,revision:state.revision+1};
          else if(c.action==='start')state={...state,title:'模擬の試行・操作確認',instruction:'この画面デモはUI確認専用です。実際の順序・時間・判定はUnityの来訪管理を使います。',running:!state.running,kind:'trial',status:state.running?'ready':'active',canStart:true,revision:state.revision+1};
          else if(c.action==='finishTrial')state={...state,running:false,status:'ready',revision:state.revision+1};
          else if(c.action==='assignRole')state={...state,bindings:state.bindings.map((v:string,i:number)=>i===c.number?c.value:v),revision:state.revision+1};
          else if(c.action==='terminate')state={...state,status:'terminated',running:false,canStart:false,revision:state.revision+1};
          else {state={...state,revision:state.revision+1};}
        }
      }
    }catch{}await new Promise(accept=>setTimeout(accept,250));}
  };
  void loop(); return Object.assign(()=>{running=false;}, {setSnapshot:(value:any)=>{state=value;}});
}
