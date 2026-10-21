import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runtimeFingerprint } from './console-validation.mts';
import { createConsoleServer } from './experiment-console.mts';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const sourceFingerprint=runtimeFingerprint(root);
const duration=Number(process.argv[2] || 5400);
let stopRequested=false;process.once('SIGINT',()=>{stopRequested=true;});process.once('SIGTERM',()=>{stopRequested=true;});
if(!Number.isInteger(duration)||duration<1)throw new Error('実時間の秒数を指定してください。');
const output=join(root,'.local','console-integration-'+new Date().toISOString().replace(/[:.]/g,'-'));
mkdirSync(output,{recursive:true});
const app=createConsoleServer({dataDirectory:join(output,'recovered-records')});await app.listen();
const page=await fetch('http://127.0.0.1:8765/');const html=await page.text();const cookie=page.headers.get('set-cookie')!.split(';')[0];
const csrf=/name="csrf" content="([a-f0-9]+)"/.exec(html)![1];
async function api(path:string,body?:any){const response=await fetch('http://127.0.0.1:8765/api/'+path,{method:body===undefined?'GET':'POST',headers:{cookie,'X-CSRF-Token':csrf,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});const data=await response.json();if(!response.ok)throw new Error(data.error);return data;}
const executable=join(root,'.local/builds/feedback-demo/FeedbackDemo.x86_64');if(!existsSync(executable))throw new Error('先にjust unity-buildを実行してください。');
const player=spawn(executable,['-batchmode','-nographics','-logFile',join(output,'player.log'),'--research-console-smoke','--research-fixture-root',join(output,'fixture')],{stdio:'ignore',shell:false});
let exited=false;player.on('exit',()=>{exited=true;});
const started=performance.now();let paired=false,pending=false,lastStage='',commands=0,polls=0,finished=false,lastResult='',exported=false,lastProgress=0;
const history:any[]=[];
const checkedRecordings=new Set<string>();
console.log('Unity + PCコンソール実時間試験: '+output);
try{
  while((performance.now()-started)/1000<duration){
    if(stopRequested)throw new Error('実施者が検証を中断しました。');
    if(exited)throw new Error('Unity playerが途中終了しました。');
    await api('heartbeat',{});const view=await api('state');polls++;
    if(!paired){const connection=join(output,'fixture/connection.json');if(view.candidates.length&&existsSync(connection)){const info=JSON.parse(readFileSync(connection,'utf8'));await api('pair',{deviceId:view.candidates[0].deviceId,code:info.pairCode});paired=true;}}
    const s=view.state;
    if(view.result?.id&&view.result.id!==lastResult){lastResult=view.result.id;pending=false;if(!view.result.ok)throw new Error(view.result.message);}
    const command=async(action:string,value='',number=0)=>{if(pending||view.pending)return;await api('command',{visitId:s?.visitId||'',revision:s?.revision||0,action,value,number});pending=true;commands++;};
    if(s&&view.connected&&!pending&&!view.pending){
      const stage=s.title+'/'+s.status;if(stage!==lastStage){lastStage=stage;history.push({elapsed:(performance.now()-started)/1000,stage});writeFileSync(join(output,'progress.json'),JSON.stringify({durationRequested:duration,elapsed:(performance.now()-started)/1000,stage,polls,commands,finished},null,2));console.log(Math.floor((performance.now()-started)/1000)+'s '+stage);}
      if(!s.visitId)await command('createVisit','1',1);
      else if(s.status==='interrupted')throw new Error('模擬来訪が中断しました: '+s.lastReason+' / '+s.error);
      else if(s.quizAwaiting){const errors=s.quizModality==='Auditory'?[-12,0,12,-3,8,-8,3,-10,0,10]:[0,8,-8,3,-10,10,-3,12,0,-12];const error=errors[s.quizAttempt===1?s.quizQuestion-1:10-s.quizQuestion];await command('answer',error< -5?'Short:longer':error>5?'Long:shorter':'Target:maintain');}
      else if(s.kind==='allocation')await command('assignGroup','Visual');
      else if(s.kind==='questionnaire')await command('questionnaire',JSON.stringify({values:[10,20,10,20,10,5,1,1],present:Array(8).fill(true),notes:'synthetic integration fixture only'}));
      else if(s.status==='done'){finished=true;if(!exported){await command('export');exported=true;}}
      else if(s.canStart)await command(s.running?'finishTrial':'start');
    }
    const elapsed=(performance.now()-started)/1000;
    if(elapsed-lastProgress>=60){
      const sessions=join(output,'fixture/ResearchSessions');
      if(existsSync(sessions))for(const name of readdirSync(sessions)){
        const folder=join(sessions,name);
        if(!checkedRecordings.has(name)&&existsSync(join(folder,'completed.json'))){
          if(readFileSync(join(folder,'events.csv'),'utf8').split('\n').some(line=>line.includes(',input_rejected,')&&line.includes(',invalid_input_time,')))throw new Error('正常な合成入力に時刻拒否があります: '+name);
          checkedRecordings.add(name);
        }
      }
      lastProgress=elapsed;writeFileSync(join(output,'progress.json'),JSON.stringify({durationRequested:duration,elapsed,stage:lastStage,polls,commands,finished},null,2));
      console.log(Math.floor(elapsed)+'s 稼働継続・通信確認 '+polls+'回 / '+lastStage+(finished?' / 来訪完了・回収後':''));
    }
    await new Promise(accept=>setTimeout(accept,500));
  }
  const view=await api('state');
  const elapsed=(performance.now()-started)/1000;
  const sessions=join(output,'fixture/ResearchSessions');
  const folders=existsSync(sessions)?readdirSync(sessions).map(name=>join(sessions,name)):[];
  const completedTrials=folders.filter(path=>existsSync(join(path,'completed.json'))).length;
  const rejectedTimestamps=folders.reduce((sum,path)=>sum+readFileSync(join(path,'events.csv'),'utf8').split('\n').filter(line=>line.includes(',input_rejected,')&&line.includes(',invalid_input_time,')).length,0);
  const passed=paired&&polls>duration*.8&&rejectedTimestamps===0&&(duration<2700||finished)&&(duration<2700||view.archives.length>0)&&(duration<2700||completedTrials===20);
  const report={passed,runtimeSourceFingerprint:sourceFingerprint,synthetic:true,physicalDeviceMeasured:false,realElapsedSeconds:elapsed,requestedSeconds:duration,polls,commands,finished,completedTrials,rejectedTimestamps,archives:view.archives,history,sourceCommit:(await import('node:child_process')).spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).stdout.trim()};
  writeFileSync(join(output,'result.json'),JSON.stringify(report,null,2));if(!passed)throw new Error('来訪の完了・回収条件を満たしません。');console.log('実時間試験に成功: '+elapsed.toFixed(1)+'秒');
}catch(error){writeFileSync(join(output,'failure.json'),JSON.stringify({error:String(error),elapsed:(performance.now()-started)/1000,history},null,2));throw error;}
finally{player.kill('SIGTERM');await new Promise(accept=>setTimeout(accept,1000));await app.close();}
