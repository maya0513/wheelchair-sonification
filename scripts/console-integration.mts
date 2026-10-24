import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runtimeFingerprint } from './console-validation.mts';
import { createConsoleServer } from './experiment-console.mts';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const duration=Number(process.argv[2] || 180), soak=duration>=5400;
if(!Number.isInteger(duration)||duration<1)throw new Error('秒数を指定してください。');
const output=join(root,'.local','console-integration-'+new Date().toISOString().replace(/[:.]/g,'-'));mkdirSync(output,{recursive:true});
const app=createConsoleServer({dataDirectory:join(output,'recovered-records')});await app.listen();
const endpoint='http://127.0.0.1:8765';const page=await fetch(endpoint);const html=await page.text();const cookie=page.headers.get('set-cookie')!.split(';')[0];const csrf=/name="csrf" content="([a-f0-9]+)"/.exec(html)![1];
async function api(path:string,body?:any){const response=await fetch(endpoint+'/api/'+path,{method:body===undefined?'GET':'POST',headers:{cookie,'X-CSRF-Token':csrf,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});const data=await response.json();if(!response.ok)throw new Error(data.error);return data;}
const executable=join(root,'.local/builds/feedback-demo/FeedbackDemo.x86_64');if(!existsSync(executable))throw new Error('先にjust unity-buildを実行してください。');
const player=spawn(executable,['-batchmode','-nographics','-logFile',join(output,'player.log'),'--research-fixture-root',join(output,'fixture')],{stdio:'ignore',shell:false});
let exited=false,stopping=false;player.once('exit',()=>{exited=true;});const started=performance.now();
process.once('SIGINT',()=>{stopping=true;});process.once('SIGTERM',()=>{stopping=true;});
const sleep=(ms:number)=>new Promise(a=>setTimeout(a,ms));let latest:any;
async function poll(){if(exited||stopping)throw new Error('継続確認を中断しました。');if(performance.now()-started>duration*1000)throw new Error('時間上限内に統合確認が終わりませんでした。');await api('heartbeat',{});latest=await api('state');return latest;}
async function waitUntil(check:(v:any)=>boolean){while(true){const v=await poll();if(check(v))return v;await sleep(150);}}
async function command(action:string,value='',number=0){const v=await waitUntil(v=>v.connected&&!v.pending&&!v.state?.transferring);const accepted=await api('command',{visitId:v.state.visitId,revision:v.state.revision,action,value,number});const reply=await waitUntil(v=>v.result?.id===accepted.id);if(!reply.result.ok)throw new Error(reply.result.message);return reply.state;}
try{
  console.log('短時間のUnity＋PC・手動操作フロー確認: '+output);
  await waitUntil(v=>v.candidates.length&&existsSync(join(output,'fixture/connection.json')));
  const info=JSON.parse(readFileSync(join(output,'fixture/connection.json'),'utf8'));await api('pair',{deviceId:latest.candidates[0].deviceId,code:info.pairCode});
  await command('createVisit','SYNTHETIC_ONLY');await command('setGroup','Visual');
  const trials:any[]=[];
  for(const phase of [1,2,3,5]) {
    const tasks=phase===2?[1,2,1,2,1,2]:[2,2,1,1];
    for(let i=0;i<tasks.length;i++){
      await command('setSelection',JSON.stringify({phase,task:tasks[i],repetition:phase===2?Math.floor(i/2)+1:i%2+1}));
      const s=await command('start');await sleep(150);
      if(phase===2)await command('trainingEnd');
      await command('finishTrial');await waitUntil(v=>!v.state?.transferring&&v.state?.exportStatus?.includes('照合済み'));
      trials.push({trialId:s.trialId,phase,task:tasks[i]});
      console.log('試行 '+trials.length+'/18 記録・試行別回収');
    }
  }
  await api('record',{visitId:latest.state.visitId,entry:{id:'integration-answer',type:'understanding',notes:'synthetic fixture, no participant'}});
  const archives=(await poll()).archives;for(const a of archives){const actual=createHash('sha256').update(readFileSync(join(app.directory,a.name))).digest('hex');if(actual!==a.sha256)throw new Error('PC回収チェックサム不一致');}
  if(soak)while((performance.now()-started)/1000<duration-1){await poll();await sleep(500);}
  const report={passed:trials.length===18&&archives.length===18,synthetic:true,physicalDeviceMeasured:false,realElapsedSeconds:(performance.now()-started)/1000,requestedSeconds:duration,finished:true,runtimeSourceFingerprint:runtimeFingerprint(root),trials,archives,waitingForTwentyMinuteRest:false};
  writeFileSync(join(output,'result.json'),JSON.stringify(report,null,2));if(!report.passed)throw new Error('記録数・回収数が一致しません。');console.log('統合確認に成功: '+report.realElapsedSeconds.toFixed(1)+'秒');
}catch(error){writeFileSync(join(output,'failure.json'),JSON.stringify({error:String(error),elapsed:(performance.now()-started)/1000},null,2));throw error;}
finally{player.kill('SIGTERM');await sleep(500);await app.close();}
