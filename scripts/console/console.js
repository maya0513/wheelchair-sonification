'use strict';
const $ = id => document.getElementById(id);
const csrf = document.querySelector('meta[name=csrf]').content;
let snapshot, state, busy = false, page = 'progress', previousQuestion = '', shownVisit = '', completedVisit = '';
const text = (id, value) => { $(id).textContent = value ?? ''; };
const seconds = value => `${Math.floor(Math.max(0, value || 0) / 60)}:${String(Math.floor(Math.max(0, value || 0) % 60)).padStart(2,'0')}`;
function showPage(next) { page = next; document.querySelectorAll('.page').forEach(e => { e.hidden = e.id !== next; }); document.querySelectorAll('[data-page]').forEach(e => e.classList.toggle('selected', e.dataset.page === next)); }
async function api(path, body) {
  const response = await fetch('/api/' + path, { method: body === undefined ? 'GET' : 'POST', headers: body === undefined ? {} : { 'Content-Type':'application/json','X-CSRF-Token':csrf }, body: body === undefined ? undefined : JSON.stringify(body), cache:'no-store' });
  const value = await response.json(); if (!response.ok) throw new Error(value.error || '処理に失敗しました。'); return value;
}
async function command(action, value = '', number = 0, flag = false) {
  if (busy && action !== 'abort') return; busy = true; text('message','');
  try { await api('command',{ action,value,number,flag,visitId:state?.visitId || '', revision:state?.revision || 0 }); }
  catch(error){ text('message',error.message); busy = false; }
}
function render(data) {
  snapshot = data; state = data.state;
  text('mode',data.demo ? '模擬モード · 実機計測不可' : '実機操作'); $('mode').classList.toggle('demo',data.demo);
  $('pairing').hidden = data.paired; $('pair').disabled = data.candidates.length===0;
  const select = $('candidate'), chosen = select.value;
  select.replaceChildren(...data.candidates.map(c => { const o = document.createElement('option'); o.value=c.deviceId; o.textContent=c.name; return o; }));
  if ([...select.options].some(o=>o.value===chosen)) select.value=chosen;
  const live = data.connected && data.operatorConnected;
  text('connection',live ? `接続中 · 更新 ${Math.round((data.staleMs || 0)/100)/10}秒前` : data.paired ? '通信が途絶えています。FB停止・未完了保存を確認し、口頭で安全停止させてください。' : '接続待ち · ゴーグルとPCを同じホットスポットへ接続してください。');
  $('connection').classList.toggle('bad',!live);
  if (data.result && data.pending===0) { busy=false; if (!data.result.ok) text('message',data.result.message); }
  if (!state) { $('primary').disabled=true; return; }
  text('participant',state.participantId ? `${state.participantId} · ${state.group === 'unallocated' ? '割付前' : state.group} · 主要側 ${state.majorSide==='left'?'左':'右'}` : '来訪未作成');
  if(state.visitId&&state.visitId!==shownVisit){
    shownVisit=state.visitId;$('major').value=state.majorSide||'right';$('group').value='';
    for(let i=0;i<8;i++)$('scale-'+i).value='';
    $('question-notes').value='';$('rest-seconds').value='';$('abort-reason').value='operator_stop';
  }
  if(state.visitId&&['done','terminated'].includes(state.status)&&completedVisit!==state.visitId){completedVisit=state.visitId;$('participant-number').value=Number(state.participantId.replace('P',''))+1;$('enrollment-number').value=Number($('enrollment-number').value)+1;}
  text('elapsed',`来訪 ${seconds(state.elapsed)} / 90:00`); text('title',state.title);
  text('instruction',state.instruction); text('target',state.kind==='target'?(!state.targetAvailable?`基準量不足（${state.baselineFirstCount}本＋${state.baselineSecondCount}本）。目標は未算定です。正常な少数プッシュで追加走行は行いません。`:`有効角 ${state.baselineFirstCount}本＋${state.baselineSecondCount}本 · 基準 ${Number(state.baselineDegrees).toFixed(1)}° → 目標 ${Number(state.targetDegrees).toFixed(1)}°（増分 ${Number(state.incrementDegrees).toFixed(1)}°）`):''); text('timer',state.countdownRemaining>0 ? `準備 ${Math.ceil(state.countdownRemaining)}秒` : state.remaining>0 ? `残り ${seconds(state.remaining)}` : state.running ? `走行 ${seconds(state.trialElapsed)}` : '');
  $('health').replaceChildren(...[['追跡',`${state.tracked}/5`,state.tracked===5],['校正',state.calibrated?'有効':'要確認',state.calibrated],['保存',state.saveHealthy?'正常':'停止',state.saveHealthy],['入力',state.sourceKind==='vive_ultimate_tracker'?'実測':state.sourceKind==='synthetic_pose_fixture'?'合成・技術確認':state.sourceKind,state.sourceKind==='vive_ultimate_tracker']].map(([name,value,ok])=>{const e=document.createElement('span');e.textContent=`${name} ${value}`;if(!ok)e.classList.add('bad');return e;}));
  $('primary').textContent=state.primaryLabel || '接続を確認'; $('primary').disabled=!live||busy||!state.canStart;
  $('use-saved-questionnaire').hidden=!state.questionnaireStored; $('save-questionnaire').disabled=!!state.questionnaireStored; $('skip-questionnaire').disabled=!!state.questionnaireStored; $('quiz').hidden=!state.quizAwaiting; $('allocation').hidden=state.kind!=='allocation'; $('questionnaire').hidden=state.kind!=='questionnaire';
  text('quiz-title',`${state.quizModality==='Auditory'?'音':'表示'} ${state.quizQuestion}/10 · ${state.quizAttempt}回目の口頭回答`);
  const question=`${state.visitId}/${state.quizModality}/${state.quizAttempt}/${state.quizQuestion}`;
  if(previousQuestion!==question){$('answer-state').value='';$('answer-action').value='';previousQuestion=question;}
  text('export-status',state.exportStatus); text('calibration',state.calibrationInstruction); text('record-counts',state.recordCounts); text('reason',state.lastReason || '中断理由なし');
  text('save-location',`PC回収先：${data.dataDirectory}`);text('diagnostics',JSON.stringify(state,null,2));
  const roles=$('roles');
  if(!roles.children.length && state.devices){for(let i=0;i<5;i++){const row=document.createElement('div');row.className='role';const name=document.createElement('strong');name.textContent=['左手首','右手首','左車輪','右車輪','車体'][i];const field=document.createElement('select');field.id='role-'+i;const button=document.createElement('button');button.textContent='この機器を割当';button.onclick=()=>command('assignRole',field.value,i);row.append(name,field,button);roles.append(row);}}
  for(let i=0;i<5;i++){const field=$('role-'+i);if(!field)continue;const remembered=field.value;const options=(state.devices||[]).map(d=>{const o=document.createElement('option');o.value=d.key;o.textContent=`${d.key} · ${d.valid?'追跡中':'無効'} · ${Math.round(d.speed*100)} cm/s`;return o;});field.replaceChildren(...options);const target=remembered||(state.bindings||[])[i];if([...field.options].some(o=>o.value===target))field.value=target;}
  $('archives').replaceChildren(...data.archives.map(a=>{const row=document.createElement('div');row.className='archive';const link=document.createElement('a');link.href='/api/archive/'+a.id;link.textContent='回収済みZIPを保存';const note=document.createElement('p');note.textContent=`${a.name} · ${a.size} bytes · SHA-256 ${a.sha256}`;row.append(link,note);return row;}));
  for(const element of document.querySelectorAll('#setup button,#setup input,#setup select')) element.disabled=!live||busy||state.running||state.busy;
  for(const id of ['recover','review-stop-reason','retry','skip','verify-rest','terminate','export','export-usb','confirm-stopped','assign-group','answer','save-questionnaire','skip-questionnaire','use-saved-questionnaire']) $(id).disabled=!live||busy||state.busy||(['save-questionnaire','skip-questionnaire'].includes(id)&&state.questionnaireStored)||(id==='assign-group'&&!$('group').value)||(id==='retry'&&!state.canRetry)||(id==='skip'&&!state.canSkip)||(id==='export'&&state.running);
  $('abort').disabled=!live;
}
async function poll(){try{await api('heartbeat',{});render(await api('state'));}catch(error){text('connection','通信エラー：'+error.message);$('connection').classList.add('bad');document.querySelectorAll('main button,#abort').forEach(e=>e.disabled=true);}finally{setTimeout(poll,500);}}
for(const b of document.querySelectorAll('[data-page]'))b.onclick=()=>showPage(b.dataset.page);
$('pair').onclick=async()=>{try{await api('pair',{deviceId:$('candidate').value,code:$('pair-code').value});text('message','');}catch(e){text('message',e.message);}};
$('primary').onclick=()=>command(state.running?'finishTrial':'start');
$('create').onclick=()=>command('createVisit',$('enrollment-number').value,Number($('participant-number').value));
$('resume').onclick=()=>command('resumeVisit');
$('abort').onclick=()=>command('abort',$('abort-reason').value);
$('capture').onclick=()=>command('calibrate');$('reset-calibration').onclick=()=>command('resetCalibration');
$('set-volume').onclick=()=>command('setVolume','',Number($('volume').value));
$('set-major').onclick=()=>command('setMajor',$('major').value);
$('set-input').onclick=()=>command('setInput',$('input-source').value);
for(const b of document.querySelectorAll('[data-quick]'))b.onclick=()=>command('quick',$('quick-modality').value+':'+b.dataset.quick);
$('assign-group').onclick=()=>command('assignGroup',$('group').value);
$('answer').onclick=()=>{if(!$('answer-state').value||!$('answer-action').value){text('message','状態と次のこぎ方の両方を入力してください。');return;}command('answer',$('answer-state').value+':'+$('answer-action').value);};
for(const [id,action] of [['recover','recover'],['retry','retry'],['skip','skip'],['confirm-stopped','confirmStopped'],['export','export'],['export-usb','exportUsb'],['terminate','terminate']])$(id).onclick=()=>command(action,'operator_confirmed');
$('verify-rest').onclick=()=>command('verifyRest','',Number($('rest-seconds').value));
const scaleNames=['知的・知覚的要求','身体的要求','時間的要求','作業成績（できなかったほど高い）','努力','フラストレーション','装着不快 0–10','疲労 0–10'];
for(let i=0;i<scaleNames.length;i++){const label=document.createElement('label');label.textContent=scaleNames[i];const field=document.createElement('input');field.type='number';field.min='0';field.max=i<6?'100':'10';field.step=i<6?'5':'1';field.id='scale-'+i;label.append(field);$('scales').append(label);}
$('save-questionnaire').onclick=()=>command('questionnaire',JSON.stringify({values:scaleNames.map((_,i)=>$('scale-'+i).value===''?0:Number($('scale-'+i).value)),present:scaleNames.map((_,i)=>$('scale-'+i).value!==''),notes:$('question-notes').value}));
$('skip-questionnaire').onclick=()=>command('skipQuestionnaire','time_limit');
showPage('progress');poll();

$('use-saved-questionnaire').onclick=()=>command('useSavedQuestionnaire');
$('terminate').onclick=()=>command('terminate',$('abort-reason').value);

$('review-stop-reason').onclick=()=>command('reviewStopReason',$('abort-reason').value);
