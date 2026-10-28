import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync, rmSync, renameSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createConsoleServer } from './experiment-console.mts';
import { demoSnapshot, startDemoDevice, demoCode, preparationFixture } from './console-demo.mts';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
export async function captureConsole(output=join(root,'.local/console-preview')) {
  const finalOutput=output;mkdirSync(dirname(finalOutput),{recursive:true});output=mkdtempSync(join(dirname(finalOutput),'.console-preview-'));
  const fixtureRecords=join(output,'fixture-recovered-records');
  rmSync(fixtureRecords,{recursive:true,force:true}); // Do not let records from a previous capture satisfy today's checks.
  const app=createConsoleServer({demo:true,port:18765,dataDirectory:fixtureRecords});await app.listen();
  const endpoint='http://127.0.0.1:18765';const demo=await startDemoDevice(endpoint);
  const profile=mkdtempSync(join(tmpdir(),'wheelchair-browser-'));
  const chrome=spawn(process.env.CHROME_BIN||'google-chrome',['--headless=new','--no-first-run','--disable-dev-shm-usage','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:'ignore',shell:false});
  let ws:WebSocket|undefined;
  const pending=new Map<number,{accept:(v:any)=>void,reject:(e:any)=>void}>();let nextId=0;
  const sleep=(ms:number)=>new Promise(accept=>setTimeout(accept,ms));
  const call=(method:string,params:any={})=>new Promise<any>((accept,reject)=>{const id=++nextId;pending.set(id,{accept,reject});ws!.send(JSON.stringify({id,method,params}));});
  const evaluate=async(expression:string)=>{const r=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true,userGesture:true});if(r.exceptionDetails)throw new Error(JSON.stringify(r.exceptionDetails));return r.result?.value;};
  const shots:any[]=[];
  try{
    const portFile=join(profile,'DevToolsActivePort');for(let i=0;i<100&&!existsSync(portFile);i++)await sleep(100);
    if(!existsSync(portFile))throw new Error('Chromeの撮影用接続を開始できません。');
    const port=readFileSync(portFile,'utf8').split('\n')[0];const pages=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
    ws=new WebSocket(pages.find((p:any)=>p.type==='page').webSocketDebuggerUrl);
    await new Promise<void>((accept,reject)=>{ws!.onopen=()=>accept();ws!.onerror=reject;});
    ws.onmessage=event=>{const body=JSON.parse(String(event.data));if(body.id&&pending.has(body.id)){const p=pending.get(body.id)!;pending.delete(body.id);if(body.error)p.reject(new Error(JSON.stringify(body.error)));else p.accept(body.result);}};
    await call('Page.enable');await call('Runtime.enable');await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
    // Keep a handle to the real Web Audio context only in this test browser, so interruption reaches the shipped listener.
    await call('Page.addScriptToEvaluateOnNewDocument',{source:`const PreviewAudioContext=window.AudioContext;window.AudioContext=class extends PreviewAudioContext{constructor(...args){super(...args);window.previewAudioContext=this;}};`});
    await call('Page.navigate',{url:endpoint});await sleep(1500);
    await evaluate(`document.getElementById('pair-code').value='${demoCode}';document.getElementById('pair').click()`);await sleep(1500);
    const capture=async(name:string,title:string,page='progress',snapshot:any=demoSnapshot(),width=1440,height=1100,focus='')=>{
      demo.setSnapshot(snapshot);await sleep(1100);
      await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.querySelector('[data-page="${page}"]').click()`);await evaluate('window.scrollTo(0,0)');await sleep(100);
      if(focus){await evaluate(`document.querySelector(${JSON.stringify(focus)}).scrollIntoView({block:'center'})`);await sleep(100);}
      const issues=await evaluate(`Array.from(document.querySelectorAll('button,h1,.instruction,#scales label')).filter(e=>e.getClientRects().length&&e.scrollWidth>e.clientWidth+2).map(e=>e.textContent)`);
      if(issues.length)throw new Error(name+': 文字が領域から溢れています: '+issues.join(','));
      const image=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});writeFileSync(join(output,name+'.png'),Buffer.from(image.data,'base64'));
      const crops:any={};
      for(const [kind,selector] of [['title','h1'],['button',page==='setup'?'#guide-actions button':page==='records'?'#export':page==='answers'?'#save-questionnaire':'#record-start']] as const){
        const rect=await evaluate(`(()=>{const e=document.querySelector('#${page} ${selector}');if(!e||!e.getClientRects().length)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,scale:1};})()`);
        if(rect&&rect.width>0&&rect.y>=0&&rect.y+rect.height<=height){const crop=await call('Page.captureScreenshot',{format:'png',clip:rect,captureBeyondViewport:false});crops[kind]=name+'-'+kind+'.png';writeFileSync(join(output,crops[kind]),Buffer.from(crop.data,'base64'));}
      }
      shots.push({name,file:name+'.png',title,width,height,crops});
    };
    const ready=demoSnapshot({visitId:'fixture-visit',participantId:'P001',status:'停止中'});
    await capture('01-initial','接続後・新しい来訪','progress',demoSnapshot());
    await capture('02-ready','評価の手動選択','progress',ready);
    await capture('03-recording','記録と計時の区別','progress',{...ready,running:true,status:'記録中',trialId:'fixture-trial'});
    await evaluate(`document.getElementById('cue').click()`);await sleep(150);
    await evaluate(`window.previewAudioContext.suspend()`);await sleep(250);
    const operatorFile=join(output,'fixture-recovered-records/fixture-visit/operator.json');
    if(!existsSync(operatorFile)||!JSON.parse(readFileSync(operatorFile,'utf8')).entries.some((e:any)=>e.type==='timing'&&e.state==='cancelled'&&e.reason==='pc_audio_suspended'&&e.seconds===null))throw new Error('開始音の中断が未完了として保存されていません。');
    if(!await evaluate(`document.getElementById('stopwatch').textContent==='計時取消'`))throw new Error('取り消した時計が進行中のように表示されています。');
    for(let i=0;i<40&&await evaluate(`document.getElementById('cue').disabled`);i++)await sleep(50);
    if(await evaluate(`document.getElementById('cue').disabled`))throw new Error('取り消した開始合図を再予約できません。');
    await evaluate(`document.getElementById('cue').click()`);
    await capture('16-preparing','PC開始合図・準備中','progress',{...ready,running:true,status:'記録中',trialId:'fixture-trial'});await sleep(2300);
    await capture('17-timing-running','PCの正式時計・手動停止前','progress',{...ready,running:true,status:'記録中',trialId:'fixture-trial'});
    if(!await evaluate(`parseFloat(document.getElementById('stopwatch').textContent)>0`))throw new Error('開始合図後にPC時計が開始していません。');
    if(await evaluate(`document.getElementById('message').textContent.includes('取り消し')`))throw new Error('再予約後も取消メッセージが残っています。');
    await evaluate(`document.getElementById('time-stop').focus()`);
    await call('Input.dispatchKeyEvent',{type:'keyDown',key:' ',code:'Space',windowsVirtualKeyCode:32});
    await call('Input.dispatchKeyEvent',{type:'keyUp',key:' ',code:'Space',windowsVirtualKeyCode:32});await sleep(250);
    if(!existsSync(operatorFile)||!JSON.parse(readFileSync(operatorFile,'utf8')).entries.some((e:any)=>e.type==='timing'&&e.seconds>0&&e.physicalAudioOnsetMeasured===false))throw new Error('PCの正式計時が保存されていません。');
    if(!await evaluate(`document.getElementById('cue').disabled`))throw new Error('計時済み試行の開始合図が再び有効になっています。');
    await capture('04-training','訓練終了と記録終了','progress',{...ready,running:true,phase:'Training',status:'訓練記録中',trialId:'fixture-trial'});
    await capture('05-training-end','FB終了後も安全停止まで記録','progress',{...ready,running:true,phase:'Training',trainingEnded:true,status:'記録中'});
    await capture('06-small-pc','小さいPC画面','progress',ready,1000,900);
    await capture('07-settings','入力・5台・左右軸校正','setup',ready);
    await capture('08-understanding','左右の見本と口頭回答','answers',ready);
    await capture('09-questionnaire','6領域と未回答の区別','answers',ready,1440,1100,'#scales');
    await capture('10-records','保存先と回収','records',ready);
    await capture('11-missing','手首欠測だけでは提示を止めない','progress',{...ready,devices:ready.devices.map((d:any,i:number)=>({...d,valid:i>=2}))});
    await capture('12-interrupted','通信・中断後の手動判断','progress',{...ready,status:'未完了',error:'operator_connection_lost'});
    await capture('13-export-failure','回収失敗・元データを保持','records',{...ready,exportStatus:'転送失敗。元データは保持。再回収できます。'});
    await capture('14-export-success','試行別コピー・照合済み','records',{...ready,exportStatus:'PCへコピー・SHA-256照合済み。元記録を保持。'});
    const laptop={...ready,running:true,status:'記録中',trialId:'laptop-live-trial'};
    await capture('18-laptop','ノートPC・主操作が画面内','progress',laptop,1366,768);
    shots.pop(); // The final image below is the evidence for this viewport, after the cue starts.
    await evaluate(`document.getElementById('cue').click()`);await sleep(3300);
    await capture('18-laptop','ノートPC・計時中の主操作が画面内','progress',laptop,1366,768);
    const offscreen=await evaluate(`['cue','time-stop','record-end','abort'].filter(id=>{const e=document.getElementById(id);if(!e.getClientRects().length)return false;const r=e.getBoundingClientRect();return r.bottom>innerHeight||r.left<0||r.right>innerWidth;})`);
    if(offscreen.length)throw new Error('ノートPCで主操作が見切れます: '+offscreen.join(','));
    await evaluate(`document.getElementById('time-stop').click()`);
    for(const [index,stage] of ['Home','Forward','Candidate','Manual','Paused','Result'].entries())await capture((19+index)+'-preparation-'+stage.toLowerCase(),'準備・'+stage,'setup',{...ready,preparation:preparationFixture(stage)},1366,900);
    app.options.demo=false;const orphan={...ready,running:true,trialId:'reloaded-unowned-trial'};demo.setSnapshot(orphan);
    await call('Page.reload');await sleep(1400);
    await capture('25-reloaded','再読込後の時計を誤って再開しない','progress',orphan,1366,768);
    if(!await evaluate(`document.getElementById('cue').disabled&&document.getElementById('record-end').disabled&&document.getElementById('next-action').textContent.includes('引き継げません')`))throw new Error('再読込後の時計が継続できるように見えます。');
    app.options.demo=true;
    await evaluate(`document.getElementById('scale-0').value='95';document.getElementById('question-notes').value='previous participant fixture'`);
    demo.setSnapshot({...ready,visitId:'second-fixture-visit',participantId:'P002'});await sleep(1100);
    if(!await evaluate(`document.getElementById('scale-0').value===''&&document.getElementById('question-notes').value===''`))throw new Error('次の参加者へ回答が残っています。');
    demo();await sleep(3600);await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.querySelector('[data-page="progress"]').click();window.scrollTo(0,0)`);await sleep(100);const stale=await call('Page.captureScreenshot',{format:'png'});writeFileSync(join(output,'15-disconnected.png'),Buffer.from(stale.data,'base64'));shots.push({name:'15-disconnected',file:'15-disconnected.png',title:'通信の鮮度・切断',width:1440,height:1100,crops:{}});
    const commit=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).stdout.trim();const dirty=!!spawnSync('git',['status','--porcelain'],{cwd:root,encoding:'utf8'}).stdout.trim();
    const manifest={passed:true,source_commit:commit,source_dirty:dirty,rendering:'Actual Chrome rendering of the shipped operator frontend; synthetic device fixtures',physical_device_measured:false,shots};
    writeFileSync(join(output,'manifest.json'),JSON.stringify(manifest,null,2));
    await call('Page.navigate',{url:'about:blank'});await sleep(100);
    await call('Page.setDocumentContent',{frameId:(await call('Page.getFrameTree')).frameTree.frame.id,html:'<!doctype html><meta charset="utf-8"><style>body{margin:24px;background:#eee;font:18px sans-serif}main{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}article{background:white;padding:12px}img{width:100%;height:270px;object-fit:contain}h2{font-size:18px;margin:0 0 8px}</style><h1>実施者画面一覧（合成入力・実ブラウザー）</h1><main>'+shots.map(s=>'<article><h2>'+s.title+'</h2><img src="data:image/png;base64,'+readFileSync(join(output,s.file)).toString('base64')+'"></article>').join('')+'</main>'});
    await call('Emulation.setDeviceMetricsOverride',{width:1440,height:2200,deviceScaleFactor:1,mobile:false});await sleep(300);
    const overview=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'overview.png'),Buffer.from(overview.data,'base64'));
    writeFileSync(join(output,'index.html'),'<!doctype html><html lang="ja"><meta charset="utf-8"><title>実施者画面の確認</title><style>body{font-family:system-ui;background:#eee;margin:30px}article{background:white;padding:20px;margin:20px 0}img{max-width:100%;border:1px solid #aaa}a{color:#1756b8}</style><h1>実施者画面の実ブラウザー画像</h1><p>合成データを使用。実機・現地の検証ではありません。Source: '+commit+'</p><p><a href="overview.png">画面一覧PNG</a></p>'+shots.map(s=>'<article><h2>'+s.title+'</h2><p>'+s.width+'×'+s.height+'</p><a href="'+s.file+'"><img src="'+s.file+'"></a>'+Object.values(s.crops).map(f=>'<p><a href="'+f+'"><img src="'+f+'"></a></p>').join('')+'</article>').join('')+'</html>');
    rmSync(finalOutput+'.previous',{recursive:true,force:true});if(existsSync(finalOutput))renameSync(finalOutput,finalOutput+'.previous');renameSync(output,finalOutput);
    console.log('実施者画面を撮影しました: '+join(finalOutput,'index.html'));return finalOutput;
  } finally {demo();ws?.close();chrome.kill('SIGTERM');await app.close();await sleep(300);rmSync(profile,{recursive:true,force:true});}
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url))captureConsole().catch(error=>{console.error(error);process.exitCode=1;});
