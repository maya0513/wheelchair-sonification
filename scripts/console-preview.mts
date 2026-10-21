import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createConsoleServer } from './experiment-console.mts';
import { demoSnapshot, startDemoDevice, demoCode } from './console-demo.mts';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
export async function captureConsole(output=join(root,'.local/console-preview')) {
  mkdirSync(output,{recursive:true});
  const app=createConsoleServer({demo:true,port:18765,dataDirectory:join(output,'fixture-recovered-records')});await app.listen();
  const endpoint='http://127.0.0.1:18765';const demo=await startDemoDevice(endpoint);
  const profile=mkdtempSync(join(tmpdir(),'wheelchair-browser-'));
  const chrome=spawn(process.env.CHROME_BIN||'google-chrome',['--headless=new','--no-first-run','--disable-dev-shm-usage','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:'ignore',shell:false});
  let ws:WebSocket|undefined;
  const pending=new Map<number,{accept:(v:any)=>void,reject:(e:any)=>void}>();let nextId=0;
  const sleep=(ms:number)=>new Promise(accept=>setTimeout(accept,ms));
  const call=(method:string,params:any={})=>new Promise<any>((accept,reject)=>{const id=++nextId;pending.set(id,{accept,reject});ws!.send(JSON.stringify({id,method,params}));});
  const evaluate=async(expression:string)=>{const r=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw new Error(JSON.stringify(r.exceptionDetails));return r.result?.value;};
  const shots:any[]=[];
  try{
    const portFile=join(profile,'DevToolsActivePort');for(let i=0;i<100&&!existsSync(portFile);i++)await sleep(100);
    if(!existsSync(portFile))throw new Error('Chromeの撮影用接続を開始できません。');
    const port=readFileSync(portFile,'utf8').split('\n')[0];const pages=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
    ws=new WebSocket(pages.find((p:any)=>p.type==='page').webSocketDebuggerUrl);
    await new Promise<void>((accept,reject)=>{ws!.onopen=()=>accept();ws!.onerror=reject;});
    ws.onmessage=event=>{const body=JSON.parse(String(event.data));if(body.id&&pending.has(body.id)){const p=pending.get(body.id)!;pending.delete(body.id);if(body.error)p.reject(new Error(JSON.stringify(body.error)));else p.accept(body.result);}};
    await call('Page.enable');await call('Runtime.enable');await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
    await call('Page.navigate',{url:endpoint});await sleep(1500);
    await evaluate(`document.getElementById('pair-code').value='${demoCode}';document.getElementById('pair').click()`);await sleep(1500);
    const capture=async(name:string,title:string,page='progress',snapshot:any=demoSnapshot(),width=1440,height=1100,focus='')=>{
      demo.setSnapshot(snapshot);await sleep(1100);
      await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
      await evaluate(`document.querySelector('[data-page="${page}"]').click()`);await evaluate('window.scrollTo(0,0)');await sleep(100);
      if(focus){await evaluate(`document.querySelector(${JSON.stringify(focus)}).scrollIntoView({block:'center'})`);await sleep(100);}
      const issues=await evaluate(`Array.from(document.querySelectorAll('button,h1,.instruction')).filter(e=>e.getClientRects().length&&e.scrollWidth>e.clientWidth+2).map(e=>e.textContent)`);
      if(issues.length)throw new Error(name+': 文字が領域から溢れています: '+issues.join(','));
      const image=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});writeFileSync(join(output,name+'.png'),Buffer.from(image.data,'base64'));
      const crops:any={};
      for(const [kind,selector] of [['title','h1'],['button',page==='setup'?'#capture':page==='records'?'#export':'#primary']] as const){
        const rect=await evaluate(`(()=>{const e=document.querySelector('#${page} ${selector}');if(!e||!e.getClientRects().length)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,scale:1};})()`);
        if(rect&&rect.width>0&&rect.y>=0&&rect.y+rect.height<=height){const crop=await call('Page.captureScreenshot',{format:'png',clip:rect,captureBeyondViewport:false});crops[kind]=name+'-'+kind+'.png';writeFileSync(join(output,crops[kind]),Buffer.from(crop.data,'base64'));}
      }
      shots.push({name,file:name+'.png',title,width,height,crops});
    };
    const ready=demoSnapshot({visitId:'fixture-visit',participantId:'P001',title:'事前評価：8の字 1/2',instruction:'共通教示と動画を確認し、開始位置での静止と安全を確認してください。',kind:'trial',status:'ready',canStart:true,primaryLabel:'この試行を開始',majorSide:'right'});
    await capture('01-setup','初期・来訪作成','setup');
    await capture('02-ready','事前評価・次の一操作','progress',ready);
    await capture('03-countdown','実施者だけが見る準備時間','progress',{...ready,running:true,canStart:false,countdownRemaining:10});
    await capture('04-running','走行中・終了と中止','progress',{...ready,title:'訓練 1/3：直進',status:'active',running:true,canStart:false,trialElapsed:35,remaining:85,group:'Visual',targetAvailable:true,targetDegrees:70,primaryLabel:'終了・安全停止を確認'});
    await capture('05-comprehension','口頭の理解確認','progress',{...ready,title:'音と表示の理解確認',kind:'quiz',status:'quiz',canStart:false,quizAwaiting:true,quizQuestion:4});
    await capture('06-allocation','条件成立後だけ群を登録','progress',{...ready,title:'割付の登録',kind:'allocation',canStart:false});
    await capture('07-rest','20分休憩・短縮不可','progress',{...ready,title:'保持前休憩：20分',kind:'rest',status:'timer',remaining:1195,canStart:false});
    await capture('08-tracking-loss','中断・口頭の安全停止','progress',{...ready,status:'interrupted',instruction:'口頭で安全停止。記録・例外対応で原因と次の対応を確認してください。',canStart:false,tracked:4,calibrated:false,lastReason:'tracking_lost'});
    await capture('09-roles','5台の識別と校正','setup',ready,1440,1100,'#roles');
    await capture('10-records','回収・例外対応','records',{...ready,recordCounts:'試行 18件 ／ 未完了 1件。未完了も保存・回収。',lastReason:'video_failure',canRetry:true,canSkip:true});
    await capture('11-questionnaire','質問紙の実施者入力','progress',{...ready,title:'訓練全体の質問紙',kind:'questionnaire',canStart:false});
    await capture('12-narrow','小さいPC画面・1024×800','progress',ready,1024,800);
    await capture('13-save-failure','保存失敗・無提示','progress',{...ready,status:'interrupted',saveHealthy:false,canStart:false,instruction:'来訪の保存失敗。元記録を保持して停止。空き容量・権限を確認してください。',lastReason:'save_failure'});
    await capture('15-calibration','実施者が進める校正','setup',{...ready,calibrationInstruction:'車体座標 → 左右車輪 → コース → 左右手首。現在：左手首の位置を収録。'},1440,1100,'#calibration');
    await capture('16-export-success','回収・チェックサム照合後','records',{...ready,exportStatus:'PCへ転送・SHA-256照合済み。元記録はゴーグルに保持。'});
    await capture('17-export-failure','回収失敗・元記録を保持','records',{...ready,exportStatus:'転送失敗。元記録は保持。通信を確認して再試行するか、USB用ZIPを出力してください。'});
    await capture('18-saved-questionnaire','再起動後は保存済みの回答を照合','progress',{...ready,title:'訓練全体の質問紙',kind:'questionnaire',canStart:false,questionnaireStored:true},1440,1100,'#use-saved-questionnaire');
    await evaluate(`document.getElementById('scale-0').value='95';document.getElementById('question-notes').value='previous participant fixture';document.getElementById('group').value='Visual'`);
    demo.setSnapshot({...ready,visitId:'second-fixture-visit',participantId:'P002'});await sleep(1100);
    if(!await evaluate(`document.getElementById('scale-0').value===''&&document.getElementById('question-notes').value===''&&document.getElementById('group').value===''`))throw new Error('次の参加者へ回答または群の選択が残っています。');
    demo();await sleep(3600);await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
    await evaluate(`document.querySelector('[data-page="progress"]').click();window.scrollTo(0,0)`);await sleep(100);const stale=await call('Page.captureScreenshot',{format:'png'});writeFileSync(join(output,'14-disconnected.png'),Buffer.from(stale.data,'base64'));shots.push({name:'14-disconnected',file:'14-disconnected.png',title:'通信の鮮度・切断',width:1440,height:1100,crops:{}});
    const commit=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).stdout.trim();const dirty=!!spawnSync('git',['status','--porcelain'],{cwd:root,encoding:'utf8'}).stdout.trim();
    const manifest={passed:true,source_commit:commit,source_dirty:dirty,rendering:'Actual Chrome rendering of the shipped operator frontend; synthetic device fixtures',physical_device_measured:false,shots};
    writeFileSync(join(output,'manifest.json'),JSON.stringify(manifest,null,2));
    await call('Page.navigate',{url:'about:blank'});await sleep(100);
    await call('Page.setDocumentContent',{frameId:(await call('Page.getFrameTree')).frameTree.frame.id,html:'<!doctype html><meta charset="utf-8"><style>body{margin:24px;background:#eee;font:18px sans-serif}main{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}article{background:white;padding:12px}img{width:100%;height:270px;object-fit:contain}h2{font-size:18px;margin:0 0 8px}</style><h1>実施者画面一覧（合成入力・実ブラウザー）</h1><main>'+shots.map(s=>'<article><h2>'+s.title+'</h2><img src="data:image/png;base64,'+readFileSync(join(output,s.file)).toString('base64')+'"></article>').join('')+'</main>'});
    await call('Emulation.setDeviceMetricsOverride',{width:1440,height:2200,deviceScaleFactor:1,mobile:false});await sleep(300);
    const overview=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'overview.png'),Buffer.from(overview.data,'base64'));
    writeFileSync(join(output,'index.html'),'<!doctype html><html lang="ja"><meta charset="utf-8"><title>実施者画面の確認</title><style>body{font-family:system-ui;background:#eee;margin:30px}article{background:white;padding:20px;margin:20px 0}img{max-width:100%;border:1px solid #aaa}a{color:#1756b8}</style><h1>実施者画面の実ブラウザー画像</h1><p>合成データを使用。実機・現地の検証ではありません。Source: '+commit+'</p><p><a href="overview.png">画面一覧PNG</a></p>'+shots.map(s=>'<article><h2>'+s.title+'</h2><p>'+s.width+'×'+s.height+'</p><a href="'+s.file+'"><img src="'+s.file+'"></a>'+Object.values(s.crops).map(f=>'<p><a href="'+f+'"><img src="'+f+'"></a></p>').join('')+'</article>').join('')+'</html>');
    console.log('実施者画面を撮影しました: '+join(output,'index.html'));return output;
  } finally {demo();ws?.close();chrome.kill('SIGTERM');await app.close();await sleep(300);rmSync(profile,{recursive:true,force:true});}
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url))captureConsole().catch(error=>{console.error(error);process.exitCode=1;});
