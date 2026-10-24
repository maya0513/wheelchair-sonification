import { TrialClock, spaceStops, audioInterruptsClock } from './timing.mjs';
const $ = id => document.getElementById(id);
const csrf = document.querySelector('meta[name=csrf]').content;
const clock = new TrialClock(), clockSession = crypto.randomUUID();
let view, state, pendingId = '', pendingAt = 0, oldVisit = '', oldSourceKind = '', audio, nodes = [], cueInfo = {}, reminderEnd = 0, reminderName = '', lastClockState = 'idle';
const message = text => { $('message').textContent = text; };
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
  if (pendingId && action !== 'abort') throw new Error('前の操作の応答を待っています。');
  pendingId = 'sending'; pendingAt = performance.now(); render();
  try {
    const result = await api('command', { visitId: state?.visitId || '', revision: state?.revision || 0, action, value, number }); pendingId = result.id;
  } catch (e) { pendingId = ''; throw e; }
}
function safe(work) { return async () => { try { await work(); } catch (e) { message(e.message); } }; }
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
}
for (const button of document.querySelectorAll('[data-page]')) button.onclick = () => {
  for (const section of document.querySelectorAll('main>section')) section.hidden = section.id !== button.dataset.page;
  for (const tab of document.querySelectorAll('[data-page]')) tab.classList.toggle('selected', tab === button);
};
click('pair', async () => { const candidate = view.candidates[0]; if (!candidate) throw new Error('ゴーグルの接続を待ってください。'); await api('pair', { deviceId: candidate.deviceId, code: $('pair-code').value }); });
click('create', () => command('createVisit', $('participant').value.trim()));
click('apply-group', () => command('setGroup', $('group').value));
const groupNames = { Auditory:'聴覚', Visual:'視覚', NoFeedback:'追加FBなし', unallocated:'未登録' };
const phaseNames = { Preparation:'習熟', Baseline:'事前', Training:'訓練', ImmediateAssessment:'直後', Rest:'休憩', RetentionAssessment:'保持' };
const taskNames = { FigureEight:'8の字', Straight:'直進10 m', None:'未選択' };
const phases = ['Preparation','Baseline','Training','ImmediateAssessment','Rest','RetentionAssessment'], tasks = ['None','Straight','FigureEight'];
click('apply-selection', () => command('setSelection', JSON.stringify({ phase: phases.indexOf($('phase').value), task: tasks.indexOf($('task').value), repetition: Number($('repetition').value) })));
click('record-start', () => { cancelAudio(); clock.state = 'idle'; return command('start'); });
click('cue', startCue); click('time-stop', () => endClock());
click('record-end', async () => { if (['preparing','running'].includes(clock.state)) await endClock(true, 'record_ended_before_time_stop'); await command('finishTrial'); });
click('training-end', () => command('trainingEnd'));
click('abort', async () => { if (['preparing','running'].includes(clock.state)) await endClock(true, 'operator_abort'); await command('abort', 'operator_abort'); });
click('set-input', () => command('setInput', $('input-kind').value));
click('set-volume', () => command('setVolume', '', Number($('volume').value)));
click('capture', () => command('calibrate')); click('reset-calibration', () => command('resetCalibration'));
click('export', () => command('export')); click('export-usb', () => command('exportUsb'));
click('training-timer', () => { reminderEnd = performance.now() + 120000; reminderName = '訓練'; });
click('rest-timer', () => { reminderEnd = performance.now() + 1200000; reminderName = '休憩'; });
click('timer-cancel', () => { reminderEnd = 0; });
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
  $('connection').textContent = view.connected ? '接続中 ／ ' + (view.state?.devices?.filter(d=>d.valid).length || 0) + '/5台追跡' : 'ゴーグル未接続／通信の途絶';
  $('pairing').hidden = view.paired;
  $('data-path').textContent = 'PC保存先：' + view.dataDirectory;
  state = view.state;
  if (state && oldVisit !== state.visitId) { oldVisit = state.visitId; $('group').value = ['Auditory','Visual','NoFeedback'].includes(state.group) ? state.group : ''; $('phase').value = state.phase; $('task').value = state.task; $('repetition').value = state.repetition; $('understanding').value = ''; $('question-notes').value = ''; labels.forEach((_,i) => $('scale-' + i).value = ''); }
  if (state && oldSourceKind !== state.sourceKind) { oldSourceKind = state.sourceKind; $('input-kind').value = state.sourceKind?.startsWith('synthetic') ? 'synthetic' : 'actual'; }
  const busy = !!pendingId || !!view.pending, active = !!state?.running, usable = view.connected && !busy && !state?.transferring;
  $('identity').textContent = state?.visitId ? `${state.participantId} ／ ${groupNames[state.group] || state.group} ／ 来訪 ${state.visitId}` : '来訪未作成';
  $('selected').textContent = state ? `登録済み：${phaseNames[state.phase] || state.phase} ／ ${taskNames[state.task] || state.task} ／ ${state.repetition}回目` : '課題未登録';
  $('record-state').textContent = state ? `${state.status} ／ 試行 ${state.trialId || '未作成'}` : '接続待ち';
  $('record-start').disabled = !usable || active || !state?.visitId || !state?.calibrationReady || !state?.presentationReady;
  $('cue').disabled = !usable || !active || state?.phase === 'Training' || ['preparing','running'].includes(clock.state) || clock.hasResultFor(state?.trialId);
  $('time-stop').disabled = clock.tick() !== 'running';
  $('record-end').disabled = !usable || !active;
  $('training-end').disabled = !usable || !active || state?.phase !== 'Training' || state?.trainingEnded;
  $('abort').disabled = !view.connected || !active;
  for (const id of ['create','apply-group','apply-selection','set-input','set-volume','capture','reset-calibration','export','export-usb']) $(id).disabled = !usable || active;
  for (const id of ['participant','group','phase','task','repetition','input-kind','volume']) $(id).disabled = active;
  $('health').textContent = state ? `入力：${state.sourceKind} ／ 校正：${state.calibrationReady ? '有効' : '未完了'} ／ XR：${state.presentationReady ? '準備済み' : '未準備'}${state.error ? ' ／ ' + state.error : ''}` : '機器情報を待っています。';
  $('calibration').textContent = state?.calibrationInstruction || '接続待ち';
  $('export-state').textContent = state?.exportStatus || '未回収';
  $('devices').replaceChildren(...(state?.devices || []).map(d => { const e = document.createElement('div'); e.textContent = `${d.valid ? '有効' : '欠測'}　${d.key}　${d.product || ''}`; return e; }));
  $('setup-device-poses').replaceChildren(...(state?.devices || []).map(d => { const e = document.createElement('p'); const n = x => Number.isFinite(x) ? x.toFixed(2) : '—'; e.textContent = d.key + (d.valid ? `　位置 ${n(d.x)}, ${n(d.y)}, ${n(d.z)} ／ 姿勢 ${n(d.rx)}, ${n(d.ry)}, ${n(d.rz)}` : '　欠測・位置姿勢なし'); return e; }));
  const bindingsKey = JSON.stringify([state?.devices?.map(d => d.key),state?.bindings]);
  if ($('roles').dataset.key !== bindingsKey) {
    $('roles').dataset.key = bindingsKey;
    $('roles').replaceChildren(...['左手首','右手首','左車輪','右車輪','車体'].map((name,i) => {
      const row = document.createElement('div'); row.className = 'row'; const label = document.createElement('label'); label.textContent = name;
      const select = document.createElement('select'); for (const d of state?.devices || []) { const o = document.createElement('option'); o.value = o.textContent = d.key; select.append(o); } select.value = state?.bindings?.[i] || ''; label.append(select);
      const b = document.createElement('button'); b.textContent = '割り当て'; b.onclick = safe(() => command('assignRole',select.value,i)); row.append(label,b); return row;
    }));
  }
  for (const b of $('roles').querySelectorAll('button')) b.disabled = !usable || active;
  $('archives').replaceChildren(...view.archives.map(a => { const link = document.createElement('a'); link.href = '/api/archive/' + a.id; link.textContent = `${a.name} ／ SHA-256 ${a.sha256.slice(0,16)}…`; return link; }));
}
async function poll() {
  try {
    await api('heartbeat', {}); view = await api('state');
    if (pendingId && view.result?.id === pendingId) { message(view.result.ok ? '反映しました。' : view.result.message); pendingId = ''; }
    if (clock.state === 'running' || clock.state === 'preparing') {
      if (!view.connected || !view.state?.running) await endClock(true, 'hmd_or_connection_interrupted');
    }
    if (pendingId && performance.now() - pendingAt > 5000) { pendingId = ''; message('操作の応答を確認できません。最新の状態を確認してから操作してください。'); }
    render();
  } catch (e) { message('通信エラー：' + e.message); if (['running','preparing'].includes(clock.state)) { try { await endClock(true, 'pc_server_connection_lost'); } catch {} } }
}
setInterval(() => {
  const cs = clock.tick();
  if (cs === 'running' && lastClockState === 'preparing') cueInfo.softwareCueReachedAtMs = performance.now();
  lastClockState = cs;
  $('stopwatch').textContent = cs === 'cancelled' ? '計時取消' : cs === 'preparing' ? `開始まで ${Math.max(0,(clock.startedAt - performance.now()) / 1000).toFixed(1)} s` : `${clock.elapsed().toFixed(2)} s`;
  $('time-stop').disabled = cs !== 'running';
  $('reminder').textContent = reminderEnd ? (performance.now() >= reminderEnd ? `${reminderName}：目安時間です。終了は手動で操作してください。` : `${reminderName} ${Math.ceil((reminderEnd - performance.now()) / 1000)} s`) : '未開始';
}, 50);
document.querySelector('[data-page=progress]').click();
await retryUnsaved(); await poll(); setInterval(poll, 500);
