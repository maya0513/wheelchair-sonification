import { TrialClock, TrialOwnership, spaceStops, audioInterruptsClock } from './timing.mjs';
const $ = id => document.getElementById(id);
const csrf = document.querySelector('meta[name=csrf]').content;
const clock = new TrialClock(), clockSession = crypto.randomUUID();
const ownership=new TrialOwnership();let orphanedTrial=false;
let view, state, pendingId = '', pendingAt = 0, oldVisit = '', oldSourceKind = '', audio, nodes = [], cueInfo = {}, reminderEnd = 0, reminderName = '', lastClockState = 'idle';
const message = (text, error=false) => { $('message').textContent = text; $('message').hidden=!text; $('message').classList.toggle('error',error); };
async function api(path, body) {
  const response = await fetch('/api/' + path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
  const data = await response.json(); if (!response.ok) throw new Error(data.error || '通信に失敗しました。'); return data;
}
async function saveEntry(type, data) {
  if (!state?.visitId) throw new Error('先に来訪を作成してください。');
  const record = { visitId: state.visitId, entry: { id: crypto.randomUUID(), type, participantId: state.participantId, trialId: state.trialId, browserClockSession: clockSession, utc: new Date().toISOString(), ...data } };
  const key = 'wheelchair-unsaved-' + record.entry.id;
  localStorage.setItem(key, JSON.stringify(record));
  try { await api('record', record); localStorage.removeItem(key); }
  catch (e) { message('PC保存失敗。ブラウザーに未回収記録を保持：' + e.message); if (state.running) command('abort', 'pc_record_save_failure').catch(() => {}); throw e; }
}
async function retryUnsaved() {
  for (const key of Object.keys(localStorage).filter(k => k.startsWith('wheelchair-unsaved-'))) {
    try { await api('record', JSON.parse(localStorage.getItem(key))); localStorage.removeItem(key); }
    catch (e) { message('未保存のPC記録があります：' + e.message); break; }
  }
}
async function command(action, value = '', number = 0) {
  if (pendingId && action !== 'abort' && action !== 'prep.cancel') throw new Error('前の操作の応答を待っています。');
  pendingId = 'sending'; pendingAt = performance.now(); render();
  try {
    const result = await api('command', { visitId: state?.visitId || '', revision: state?.revision || 0, action, value, number }); pendingId = result.id;
    if(action==='start')ownership.requested(result.id);
  } catch (e) { pendingId = ''; throw e; }
}
function safe(work) { return async () => { try { await work(); } catch (e) { message(e.message,true); render(); } }; }
function click(id, fn) { $(id).addEventListener('click', safe(fn)); }
function cancelAudio() { for (const node of nodes) { try { node.stop(); } catch {} } nodes = []; }
async function endClock(cancel = false, reason = '') {
  const result = cancel ? clock.cancel(reason) : clock.stop();
  if (!result) return; cancelAudio();
  await saveEntry('timing', { ...result, ...cueInfo, physicalAudioOnsetMeasured: false });
  message(cancel ? '計時を取り消し、理由を保存しました。' : '正式計時を保存しました。安全停止後に記録を終了してください。');
}
async function startCue() {
  if (!view?.connected || !state?.running || state.phase === 'Training') throw new Error('評価の記録開始応答後に合図を出してください。');
  if(orphanedTrial)throw new Error('再読込前の時計を復元できません。中止して新しい記録でやり直してください。');
  if (clock.hasResultFor(state.trialId)) throw new Error('この試行は計時済みです。再試行は新しい記録として開始してください。');
  if (!audio) {
    audio = new AudioContext();
    audio.addEventListener('statechange', () => {
      if (audioInterruptsClock(clock.state, audio.state)) safe(() => endClock(true, 'pc_audio_' + audio.state))();
    });
  }
  await audio.resume();
  if (audio.state !== 'running') throw new Error('PCの音声出力を有効にしてください。');
  if (['preparing', 'running'].includes(clock.state)) return;
  cancelAudio();
  const nowMs = performance.now(), audioNow = audio.currentTime, lead = .08;
  clock.prepare((3 + lead) * 1000, state.trialId);
  cueInfo = { audioContextScheduledStartSeconds: audioNow + lead + 3, audioContextSampleSeconds: audioNow, performanceSampleMs: nowMs, softwareCueReachedAtMs: null };
  for (let i = 0; i < 4; i++) {
    const oscillator = audio.createOscillator(), gain = audio.createGain(), at = audioNow + lead + i, duration = i === 3 ? .22 : .09;
    oscillator.frequency.value = i === 3 ? 880 : 440;
    gain.gain.setValueAtTime(0, at); gain.gain.linearRampToValueAtTime(.15, at + .01); gain.gain.setValueAtTime(.15, at + duration - .01); gain.gain.linearRampToValueAtTime(0, at + duration);
    oscillator.connect(gain).connect(audio.destination); oscillator.start(at); oscillator.stop(at + duration); nodes.push(oscillator);
  }
  await saveEntry('cue_scheduled', { ...clock.result(), ...cueInfo, physicalAudioOnsetMeasured: false });
  message('開始合図を予約しました。ゴールで計時終了を押してください。');
}
for (const button of document.querySelectorAll('[data-page]')) button.onclick = () => {
  for (const section of document.querySelectorAll('main>section:not(#pairing)')) section.hidden = section.id !== button.dataset.page;
  for (const tab of document.querySelectorAll('[data-page]')) tab.classList.toggle('selected', tab === button);
  message('');
};
click('pair', async () => { const candidate = view.candidates[0]; if (!candidate) throw new Error('ゴーグルの接続を待ってください。'); await api('pair', { deviceId: candidate.deviceId, code: $('pair-code').value }); });
click('prep-cancel',()=>command('prep.cancel'));
click('short-check',()=>command('prep.recheck','output'));
click('manual-assign',()=>command('prep.select',$('manual-device').value,state?.preparation?.manualRole));
click('create', () => command('createVisit', $('participant').value.trim()));
click('apply-group', () => command('setGroup', $('group').value));
const groupNames = { Auditory:'聴覚', Visual:'視覚', NoFeedback:'追加FBなし', unallocated:'未登録' };
const deviceLabel = device => '機器 '+String.fromCharCode(65+[...(state?.devices||[])].map(d=>d.key).sort().indexOf(device.key));
const phaseNames = { Preparation:'習熟', Baseline:'事前', Training:'訓練', ImmediateAssessment:'直後', Rest:'休憩', RetentionAssessment:'保持' };
const taskNames = { FigureEight:'8の字', Straight:'直進10 m', None:'未選択' };
const phases = ['Preparation','Baseline','Training','ImmediateAssessment','Rest','RetentionAssessment'], tasks = ['None','Straight','FigureEight'];
click('apply-selection', () => command('setSelection', JSON.stringify({ phase: phases.indexOf($('phase').value), task: tasks.indexOf($('task').value), repetition: Number($('repetition').value) })));
click('record-start', () => { cancelAudio(); clock.state = 'idle'; return command('start'); });
click('cue', startCue); click('time-stop', () => endClock());
click('record-end', async () => { if(orphanedTrial)throw new Error('時計を引き継げない記録です。中止してやり直してください。');if (['preparing','running'].includes(clock.state)) await endClock(true, 'record_ended_before_time_stop'); await command('finishTrial'); });
click('training-end', () => command('trainingEnd'));
click('abort', async () => { if (['preparing','running'].includes(clock.state)) await endClock(true, 'operator_abort'); await command('abort', orphanedTrial?'browser_session_changed':'operator_abort'); });
click('set-input', () => command('setInput', $('input-kind').value));
click('set-volume', () => command('setVolume', '', Number($('volume').value)));
click('capture', () => command('calibrate')); click('reset-calibration', () => command('resetCalibration'));
click('export', () => command('export')); click('export-usb', () => command('exportUsb'));
click('training-timer', () => { reminderEnd = performance.now() + 120000; reminderName = '訓練'; });
click('rest-timer', () => { reminderEnd = performance.now() + 1200000; reminderName = '休憩'; });
click('timer-cancel', () => { reminderEnd = 0; });
const settingLabels={stillSeconds:'静止確認（秒）',minimumTravelMeters:'前後それぞれの移動（m）',minimumRotationDegrees:'車輪回転（度）',minimumAxisCoherence:'定軸性（比率）',minimumValidRatio:'有効サンプル（比率）',motionTimeoutSeconds:'動作待機上限（秒）',stillSpeedMeters:'静止の並進（m/秒）',stillAngularDegrees:'静止の回転（度/秒）',maximumBodyRotationDegrees:'車体の姿勢変化（度）',minimumWheelSeparationMeters:'左右の間隔（m）',maximumAxisForwardDot:'軸と前進方向の内積',maximumReturnAxisAngleDegrees:'前後の軸の差（度）'};
const settingsCard=document.createElement('div');settingsCard.innerHTML='<h2>準備の判定設定</h2><p class="hint">機能チェックの試作用設定です。変更後は割当・校正を取り直します。研究のFB設定とは別です。</p><div id="prep-settings" class="row"></div><button id="save-prep-settings">判定設定を保存</button> <button id="reset-prep-settings">初期値へ戻す</button><h2>チェックの詳細</h2><pre id="prep-diagnostics"></pre>';
document.querySelector('.technical-content').append(settingsCard);
for(const [key,label] of Object.entries(settingLabels)){const e=document.createElement('label');e.textContent=label;const input=document.createElement('input');input.type='number';input.step='any';input.id='setting-'+key;e.append(input);$('prep-settings').append(e);}
click('save-prep-settings',()=>{const settings=Object.fromEntries(Object.keys(settingLabels).map(k=>[k,Number($('setting-'+k).value)]));if(Object.values(settings).some(v=>!Number.isFinite(v)))throw new Error('判定設定に数値を入力してください。');return command('prep.settings',JSON.stringify(settings));});
click('reset-prep-settings',()=>command('prep.settings','default'));
click('save-understanding', async () => { await saveEntry('understanding', { notes: $('understanding').value }); message('理解確認をPCへ保存しました。'); });
const labels = ['精神的要求','身体的要求','時間的要求','達成度（低値が良）','努力','不満'];
labels.forEach((label, i) => { const e = document.createElement('label'); e.textContent = label; const input = document.createElement('input'); input.type = 'number'; input.min = '0'; input.max = '100'; input.id = 'scale-' + i; e.append(input); $('scales').append(e); });
click('save-questionnaire', async () => {
  const values = labels.map((_, i) => $('scale-' + i).value === '' ? null : Number($('scale-' + i).value));
  if (values.some(x => x !== null && (!Number.isFinite(x) || x < 0 || x > 100))) throw new Error('尺度は0–100、未回答は空欄にしてください。');
  await saveEntry('questionnaire', { values, notes: $('question-notes').value }); message('質問紙をPCへ保存しました。');
});
for (const modality of ['Auditory','Visual']) {
  const row = document.createElement('div'); row.className = 'row'; const name = document.createElement('strong'); name.textContent = modality === 'Auditory' ? '聴覚' : '視覚'; row.append(name);
  for (const [label,l,r] of [['左だけ',.5,0],['右だけ',0,.5],['左が速い',.8,.3]]) { const b = document.createElement('button'); b.textContent = label; b.onclick = safe(async () => { await command('quick', `${modality}:${l}:${r}`); if (state.visitId) await saveEntry('example', { modality, leftRps:l, rightRps:r }); }); row.append(b); }
  $('examples').append(row);
}
window.addEventListener('keydown', e => { if (spaceStops(e) && clock.tick() === 'running') { e.preventDefault(); safe(() => endClock())(); } });
function render() {
  if (!view) return;
  $('mode').textContent = view.demo ? '模擬モード・実計測には使用不可' : view.state?.sourceKind?.startsWith('synthetic') ? '合成入力・実計測には使用不可' : view.state?.sourceKind === 'vive_ultimate_tracker' ? '実測入力' : '入力確認待ち';
  const required=[2,3,4].filter(i=>view.state?.devices?.some(d=>d.key===view.state.bindings?.[i]&&d.valid)).length;
  const wrists=[0,1].filter(i=>view.state?.devices?.some(d=>d.key===view.state.bindings?.[i]&&d.valid)).length;
  $('connection').textContent = view.connected ? (required?`接続中 ／ 走行 ${required}/3 ・ 手首 ${wrists}/2`:`接続中・${view.state?.devices?.filter(d=>d.valid).length||0}台検出・割当前`) : 'ゴーグル未接続';
  $('pairing').hidden = view.paired;
  $('pair').disabled=!view.candidates?.length;
  $('pair-help').textContent=view.candidates?.length?'ゴーグルを検出しました。表示された6桁の番号を入力して承認してください。':'ゴーグルからの接続を待っています。PCのホットスポットへ接続し、アプリを開いてください。';
  $('data-path').textContent = 'PC保存先：' + view.dataDirectory;
  state = view.state;
  $('operation-status').textContent=state?.running?'試行を記録中':state?.transferring?'PCへコピー中':state?.preparation?.active?'準備チェック中':'停止中';
  const problems={operator_connection_lost:'PCとの通信が途切れたため、試行を未完了で停止しました。接続を確認し、再開する課題を選んでください。',browser_session_changed:'ブラウザーの時計を引き継げないため、未完了で中止しました。新しい記録でやり直してください。',application_pause:'ゴーグルが中断されました。再装着と追跡を確認してください。',xr_or_head_tracking_lost:'ゴーグルの追跡・表示を確認してください。復帰後も自動再開しません。',tracking_origin_changed_recalibrate:'計測原点が変わりました。校正を取り直してください。'};
  const systemProblem=state?.error?(problems[state.error]||'確認が必要です：'+state.error):state&&!state.presentationReady?state.presentationStatus||'ゴーグルの表示・追跡を確認してください。':'';
  $('system-problem').hidden=!systemProblem;$('system-problem').textContent=systemProblem;
  if (state && oldVisit !== state.visitId) { oldVisit = state.visitId; $('participant').value=state.participantId||'';$('group').value = ['Auditory','Visual','NoFeedback'].includes(state.group) ? state.group : ''; $('phase').value = state.phase; $('task').value = state.task; $('repetition').value = state.repetition; $('understanding').value = ''; $('question-notes').value = ''; labels.forEach((_,i) => $('scale-' + i).value = ''); }
  if (state && oldSourceKind !== state.sourceKind) { oldSourceKind = state.sourceKind; $('input-kind').value = state.sourceKind?.startsWith('synthetic') ? 'synthetic' : 'actual'; }
  const busy = !!pendingId || !!view.pending, active = !!state?.running, preparing=!!state?.preparation?.active, usable = view.connected && !busy && !state?.transferring;
  $('identity').textContent = state?.visitId ? `${state.participantId} ／ ${groupNames[state.group] || state.group} ／ 来訪 ${state.visitId.slice(0,8)}` : '来訪未作成';
  $('selected').textContent = state ? `${phaseNames[state.phase] || state.phase} ／ ${taskNames[state.task] || state.task} ／ ${state.repetition}回目` : '課題未登録';
  $('record-state').textContent = state ? `${state.status}${state.trialId ? ' ／ '+state.trialId.slice(0,8) : ''}` : '接続待ち';
  const draftTask=state&&($('phase').value!==state.phase||$('task').value!==state.task||Number($('repetition').value)!==state.repetition);
  const draftGroup=state&&($('group').value||'unallocated')!==state.group;
  const startReason=!view.connected?'ゴーグルを接続してください。':busy?'操作の反映を待っています。':state?.transferring?'記録をPCへコピーしています。':preparing?'準備チェックを終了してください。':!state?.visitId?'研究IDを入力し、来訪を作成してください。':$('participant').value.trim()!==state.participantId?'研究IDの変更は新しい来訪で登録してください。':draftTask?'課題の変更を登録してください。':draftGroup?'群の変更を登録してください。':state.phase==='Training'&&state.group==='unallocated'?'訓練前に群を登録してください。':!state?.calibrationReady?'準備・チェックで割当と校正を行ってください。':!state?.presentationReady?'ゴーグルの追跡と表示を確認してください。':'';
  $('record-start').disabled = !!startReason || active;
  $('start-disabled').textContent=active?'':startReason;
  $('cue').disabled = !usable || !active || orphanedTrial || state?.phase === 'Training' || ['preparing','running'].includes(clock.state) || clock.hasResultFor(state?.trialId);
  $('time-stop').disabled = clock.tick() !== 'running';
  $('record-end').disabled = !usable || !active || orphanedTrial;
  $('training-end').disabled = !usable || !active || state?.phase !== 'Training' || state?.trainingEnded;
  $('abort').disabled = !view.connected || !active;
  for (const id of ['create','apply-group','apply-selection','set-input','set-volume','capture','reset-calibration','export','export-usb']) $(id).disabled = !usable || active || preparing;
  for (const id of ['participant','group','phase','task','repetition','input-kind','volume']) $(id).disabled = active;
  $('health').textContent = state ? `入力：${state.sourceKind?.startsWith('synthetic')?'合成':'実測'} ／ 校正：${state.calibrationReady ? '有効' : '未完了'} ／ ゴーグル：${state.presentationReady ? '準備済み' : '未準備'}` : '機器情報を待っています。';
  $('calibration').textContent = state?.calibrationInstruction || '接続待ち';
  $('export-state').textContent = state?.exportStatus || '未回収';
  const cs=clock.tick(), training=state?.phase==='Training';
  $('record-start').hidden=active;
  $('cue').hidden=!active||training||['preparing','running'].includes(cs)||clock.hasResultFor(state?.trialId);
  $('time-stop').hidden=!active||cs!=='running';
  $('training-end').hidden=!active||!training||state?.trainingEnded;
  $('record-end').hidden=!active;
  $('abort').hidden=!active;
  $('next-action').textContent=!active?'記録開始のあと、開始合図を出します。':training?(state.trainingEnded?'FB停止済み。安全停止後に記録を終了します。':'目安2分。訓練終了でFBを止め、安全停止まで記録します。'):cs==='preparing'?'開始音を準備中です。':cs==='running'?'ゴールを見て計時終了、またはSpaceを押します。':clock.hasResultFor(state?.trialId)?'計時を保存しました。安全停止後に記録を終了します。':'開始合図を押すと3秒後に計時を始めます。';
  if(orphanedTrial){$('next-action').textContent='このブラウザーでは時計を引き継げません。中止して新しい記録でやり直してください。';$('cue').hidden=true;}
  renderPreparation(usable,active,busy);
  $('devices').replaceChildren(...(state?.devices || []).map(d => { const e = document.createElement('div'); e.textContent = `${d.valid ? '有効' : '欠測'}　${d.key}　${d.product || ''}`; return e; }));
  $('setup-device-poses').replaceChildren(...(state?.devices || []).map(d => { const e = document.createElement('p'); const n = x => Number.isFinite(x) ? x.toFixed(2) : '—'; e.textContent = d.key + (d.valid ? `　位置 ${n(d.x)}, ${n(d.y)}, ${n(d.z)} ／ 姿勢 ${n(d.rx)}, ${n(d.ry)}, ${n(d.rz)}` : '　欠測・位置姿勢なし'); return e; }));
  const bindingsKey = JSON.stringify([state?.devices?.map(d => d.key),state?.bindings]);
  if ($('roles').dataset.key !== bindingsKey) {
    $('roles').dataset.key = bindingsKey;
    $('roles').replaceChildren(...['左手首','右手首','左車輪','右車輪','車体'].map((name,i) => {
      const row = document.createElement('div'); row.className = 'row'; const label = document.createElement('label'); label.textContent = name;
      const select = document.createElement('select'); const blank=new Option('未割当（解除）','');select.append(blank); for (const d of state?.devices || []) { const o = document.createElement('option'); o.value = d.key; o.textContent = d.key+(d.valid?' ／ 追跡中':' ／ 欠測'); select.append(o); } select.value = state?.bindings?.[i] || ''; label.append(select);
      const b = document.createElement('button'); b.textContent = '割り当て'; b.onclick = safe(() => command('assignRole',select.value,i)); row.append(label,b); return row;
    }));
  }
  for (const b of $('roles').querySelectorAll('button')) b.disabled = !usable || active || preparing;
  $('archives').replaceChildren(...view.archives.map(a => { const link = document.createElement('a'); link.href = '/api/archive/' + a.id; link.textContent = `${a.name} ／ SHA-256 ${a.sha256.slice(0,16)}…`; return link; }));
}
function renderPreparation(usable,active,busy) {
  const p=state?.preparation;
  $('guide-title').textContent=p?.title||'ゴーグルの接続を待っています';
  $('guide-instruction').textContent=p?.instruction||'接続後に案内付きチェックを始められます。';
  $('guide-detail').textContent=p?.detail||'ゴーグル単体でも準備・チェックを行えます。';
  $('guide-step').textContent=p?.step?`準備 ${Math.min(p.step,6)} / 6`:'案内付きチェック';
  $('guide-running').textContent=busy?'変更を反映中…':p?.moving?'動作を確認中':p?.active?'チェック中':'';
  $('prep-issue').hidden=!p?.problem;$('prep-issue').textContent=p?.problem||'';
  $('guide-disabled').textContent=!view.connected?'PCから操作する場合はゴーグルを接続してください。':active?'研究試行の記録中です。終了してから準備を変更してください。':state?.transferring?'PCへのコピーが終わるまでお待ちください。':'';
  const actions=p?.actions||[];
  const key=JSON.stringify(actions);
  if($('guide-actions').dataset.key!==key){
    $('guide-actions').dataset.key=key;
    $('guide-actions').replaceChildren(...actions.map((a,i)=>{const b=document.createElement('button');b.textContent=a.label;b.className=i===0?'primary':'';b.onclick=safe(()=>command('prep.'+a.id));return b;}));
  }
  const tooFew=(state?.devices?.filter(d=>d.valid).length||0)<3&&['Home','Result'].includes(p?.stage);
  for(const [i,b] of [...$('guide-actions').children].entries())b.disabled=!usable||active||!state?.presentationReady||tooFew&&['start','retry'].includes(actions[i]?.id);
  if(usable&&tooFew)$('guide-disabled').textContent='走行用3台の追跡を待っています。ゴーグルの「設定 → 入力 → モーショントラッカー」で接続・環境スキャンを確認してください。';
  $('prep-cancel').hidden=!p?.active;$('prep-cancel').disabled=!view.connected;
  $('short-check').disabled=!usable||active||p?.active;
  $('manual-selection').hidden=p?.stage!=='Manual';
  const devicesKey=JSON.stringify(state?.devices?.map(d=>[d.key,d.valid]));
  if($('manual-device').dataset.key!==devicesKey){
    const old=$('manual-device').value;$('manual-device').dataset.key=devicesKey;
    $('manual-device').replaceChildren(new Option('目印の機器を選択',''),...(state?.devices||[]).filter(d=>d.valid).map(d=>new Option(deviceLabel(d),d.key)));$('manual-device').value=old;
  }
  $('manual-assign').disabled=!usable||active||!$('manual-device').value;
  $('role-status').replaceChildren(...[2,3,4,0,1].map(i=>{
    const key=p?.proposedBindings?.[i]||state?.bindings?.[i], device=state?.devices?.find(d=>d.key===key),row=document.createElement('div');row.className='role-row'+(device?.valid?' ok':'');
    const label=document.createElement('strong');label.textContent=['左手首','右手首','左車輪','右車輪','車体'][i];
    const status=document.createElement('span');status.textContent=!key?(i<2?'追加・未割当':'未割当'):device?.valid?`${deviceLabel(device)}・${p?.stage==='Candidate'?'候補':'追跡中'}`:'追跡待ち';row.append(label,status);return row;
  }));
  const resultKey=JSON.stringify(p?.results||[]);
  if($('prep-results').dataset.key!==resultKey){$('prep-results').dataset.key=resultKey;
    $('prep-results').replaceChildren(...(p?.results?.length?p.results.filter(item=>item.status!=='not_in_scope').map(item=>{const e=document.createElement('div');e.className='check-item '+item.status;const name=document.createElement('strong');name.textContent=item.label;const status=document.createElement('span');status.textContent=({confirmed:'確認済み',attention:'要確認',pending:'未実施',not_tested:'未実施'})[item.status]||item.status;e.title=item.detail||'';e.append(name,status);if(item.id!=='storage'){const button=document.createElement('button');button.textContent='再確認';button.className='text-button recheck';button.onclick=safe(()=>command('prep.recheck',item.id));e.append(button);}return e;}):[Object.assign(document.createElement('p'),{textContent:'まだチェックを行っていません。'})]));
  }
  for(const b of $('prep-results').querySelectorAll('button'))b.disabled=!usable||active||p?.active;
  const settingsKey=JSON.stringify(p?.settings||{});
  if($('prep-settings').dataset.key!==settingsKey){$('prep-settings').dataset.key=settingsKey;for(const key of Object.keys(settingLabels))$('setting-'+key).value=p?.settings?.[key]??'';}
  for(const input of $('prep-settings').querySelectorAll('input'))input.disabled=!usable||active||p?.active;
  $('save-prep-settings').disabled=$('reset-prep-settings').disabled=!usable||active||p?.active||!p?.settings;
  $('prep-diagnostics').textContent=JSON.stringify({checkId:p?.checkId,scope:p?.scope,input:state?.sourceKind,technicalProblem:p?.technicalProblem,valid:p?.speedValid,leftRps:p?.leftRps,rightRps:p?.rightRps,results:p?.results},null,2);
}
$('manual-device').addEventListener('change',()=>render());
async function poll() {
  try {
    await api('heartbeat', {}); view = await api('state');
    orphanedTrial=ownership.observe(view.state,view.result,view.demo);
    if (pendingId && view.result?.id === pendingId) { message(view.result.ok ? '操作を反映しました。' : view.result.message,!view.result.ok); pendingId = ''; }
    if (clock.state === 'running' || clock.state === 'preparing') {
      if (!view.connected || !view.state?.running) await endClock(true, 'hmd_or_connection_interrupted');
      else if(clock.trialId!==view.state.trialId)await endClock(true,'trial_changed');
    }
    if (pendingId && performance.now() - pendingAt > 5000) { pendingId = ''; message('操作の応答を確認できません。最新の状態を確認してから操作してください。'); }
    render();
  } catch (e) { message('通信エラー：' + e.message); if (['running','preparing'].includes(clock.state)) { try { await endClock(true, 'pc_server_connection_lost'); } catch {} } }
}
setInterval(() => {
  const cs = clock.tick();
  if (cs === 'running' && lastClockState === 'preparing') cueInfo.softwareCueReachedAtMs = performance.now();
  lastClockState = cs;
  $('stopwatch').textContent = cs === 'cancelled' ? '計時取消' : cs === 'preparing' ? `開始まで ${Math.max(0,(clock.startedAt - performance.now()) / 1000).toFixed(1)} s` : `${clock.elapsed().toFixed(1)} s`;
  $('time-stop').disabled = cs !== 'running';
  $('reminder').textContent = reminderEnd ? (performance.now() >= reminderEnd ? `${reminderName}：目安時間です。終了は手動で操作してください。` : `${reminderName} ${Math.ceil((reminderEnd - performance.now()) / 1000)} s`) : '未開始';
}, 50);
document.querySelector('[data-page=setup]').click();
await retryUnsaved(); await poll(); setInterval(poll, 500);
