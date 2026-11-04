import { createHash, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
// Synthetic device used only by the screenshot checks; not included in the operator distribution.
const preparationFixtures = JSON.parse(
  readFileSync(new URL('./console/preparation-fixtures.json', import.meta.url), 'utf8'),
);
export function preparationFixture(stage = 'Home') {
  return structuredClone(
    (stage === 'TrackingWait'
      ? preparationFixtures.trackingWait
      : stage === 'FreeResult'
        ? preparationFixtures.freeResult
        : preparationFixtures.stages.find((s: any) => s.stage === stage)) ||
      preparationFixtures.stages[0],
  );
}
const previewDeviceId = 'de000000000000000000000000000004';
export const previewCode = '123456';
export function previewSnapshot(overrides: any = {}) {
  return {
    schema: 3,
    displayAligned: true,
    canAlignDisplay: true,
    displayAlignmentReason: '',
    feedbackRangeRps: 0.75,
    eyeStatus: '取得不可',
    eyeReason: 'UI preview fixture; no eye sensor',
    eyeCalibrationConfirmed: false,
    revision: 0,
    visitId: '',
    participantId: '',
    trialId: '',
    group: 'Auditory',
    sourceKind: 'synthetic_pose_fixture',
    phase: 'Baseline',
    task: 'FigureEight',
    repetition: 1,
    status: '停止中',
    running: false,
    calibrationReady: true,
    presentationReady: true,
    trainingEnded: false,
    transferring: false,
    calibrationInstruction: '模擬校正。実測の性能を示しません。',
    exportStatus: '未回収',
    error: '',
    preparation: preparationFixture(),
    devices: Array.from({ length: 5 }, (_, i) => ({
      key: 'synthetic-' + i,
      product: '合成機器',
      runtimeDeviceId: 900 + i,
      valid: true,
      x: i * 0.2,
      y: 0.5,
      z: 0,
      rx: i * 12,
      ry: 0,
      rz: 0,
    })),
    bindings: Array.from({ length: 5 }, (_, i) => 'synthetic-' + i),
    ...overrides,
  };
}
export async function startPreviewDevice(endpoint: string, options: { snapshot?: any } = {}) {
  let token = '',
    running = true,
    state = options.snapshot || previewSnapshot(),
    result: any = null,
    lastId = '',
    advanceAt = 0,
    advanceTo = '';
  const show = (stage: string) => {
    state = { ...state, preparation: preparationFixture(stage), revision: state.revision + 1 };
  };
  const advance = (stage: string) => {
    advanceAt = performance.now() + 900;
    advanceTo = stage;
  };
  const request = async (path: string, body: any) => {
    const response = await fetch(endpoint + path, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { 'X-Device-Token': token } : {}),
      },
      body: JSON.stringify(body),
    });
    return response.json();
  };
  const loop = async () => {
    while (running) {
      try {
        if (advanceAt && performance.now() >= advanceAt) {
          advanceAt = 0;
          show(advanceTo);
        }
        if (!token) {
          const hello = await request('/device/hello', {
            schema: 3,
            deviceId: previewDeviceId,
            codeHash: createHash('sha256')
              .update(previewDeviceId + ':' + previewCode)
              .digest('hex'),
          });
          token = hello.pairToken || '';
        } else {
          // Screenshot fixture only; real availability is calculated once by ResearchVisitHost.
          const p = state.preparation;
          const reason = state.running
            ? '記録を終了してから表示位置を合わせてください。'
            : !state.presentationReady
              ? 'ゴーグルの追跡と表示を確認してください。'
              : !state.calibrationReady
                ? '走行用3台の登録・追跡と校正を確認してください。'
                : p?.moving
                  ? '走行・動作確認を終了してから表示位置を合わせてください。'
                  : ['Visual', 'Audio'].includes(p?.stage)
                    ? '見本の提示終了をお待ちください。'
                    : !p?.safeToShowPanel
                      ? '車いすを止めて、静止の確認をお待ちください。'
                      : '';
          state.canAlignDisplay = !reason;
          state.displayAlignmentReason = reason;
          const reply = await request('/device/exchange', { state, result });
          if (reply.command && reply.command.id !== lastId) {
            const c = reply.command;
            lastId = c.id;
            result = { id: c.id, ok: true, message: '画像検証用の状態へ反映しました。' };
            if (c.action.startsWith('prep.')) {
              const action = c.action.slice(5),
                stage = state.preparation?.stage;
              if (action === 'start' || action === 'register' || action === 'retry') {
                show('Still');
                advance('ForwardReady');
              } else if (['motion', 'feedback', 'free'].includes(action)) {
                show({ motion: 'MotionMenu', feedback: 'FeedbackMenu', free: 'FreeMenu' }[action]!);
              } else if (action.startsWith('check.')) {
                const item = action.slice(6);
                show(
                  item === 'audio'
                    ? 'AudioReady'
                    : item === 'visual'
                      ? 'VisualReady'
                      : ['still', 'motion'].includes(item)
                        ? 'VerifyReady'
                        : 'Verify' + item[0].toUpperCase() + item.slice(1) + 'Ready',
                );
              } else if (action === 'freeVisual' || action === 'freeAuditory') {
                show('Free');
                state.preparation.freeGroup = action === 'freeVisual' ? 'Visual' : 'Auditory';
              } else if (action === 'cancel') {
                advanceAt = 0;
                const free = state.preparation?.mode === 'free';
                show(free ? 'FreeResult' : 'Result');
                if (!free) state.preparation.problem = '画像検証用のチェックを中断しました。';
              } else if (action === 'confirmStopped') {
                show(state.preparation.mode === 'free' ? 'FreeResult' : 'Result');
                state.preparation.safeToShowPanel = true;
                state.preparation.stopConfirmedManually = true;
              } else if (action === 'wait') show('Waiting');
              else if (action === 'home') show('Home');
              else if (action === 'recheck')
                show(
                  c.value === 'audio'
                    ? 'AudioAnswer'
                    : c.value === 'visual' || c.value === 'output'
                      ? 'VisualAnswer'
                      : c.value === 'wrists'
                        ? 'WristsChoice'
                        : 'ForwardReady',
                );
              else if (action === 'manual') show('Manual');
              else if (action === 'select') {
                const p = state.preparation;
                p.proposedBindings[c.number] = c.value;
                if (c.number === 2) p.manualRole = 3;
                else if (c.number === 3) p.manualRole = 4;
                else show('ForwardReady');
              } else if (action === 'skip') {
                show('Result');
              } else if (action === 'yes' || action === 'no') show('Result');
              else if (action === 'next') {
                if (stage === 'ForwardReady') {
                  show('Forward');
                  advance('BackwardReady');
                } else if (stage === 'BackwardReady') {
                  show('Backward');
                  advance('Candidate');
                } else if (stage === 'Candidate') show('WristsChoice');
                else if (stage === 'WristsChoice') {
                  show('VerifyReady');
                } else if (stage === 'VerifyReady') {
                  show('VerifyLeft');
                  advance('Result');
                } else if (stage === 'VisualReady' || stage === 'AudioReady') {
                  show(stage === 'VisualReady' ? 'VisualAnswer' : 'AudioAnswer');
                } else show('Result');
              }
            } else if (c.action === 'createVisit')
              state = previewSnapshot({
                visitId: randomUUID(),
                participantId: c.value,
                revision: state.revision,
              });
            else if (c.action === 'start')
              state = { ...state, running: true, status: '模擬記録中', trialId: randomUUID() };
            else if (c.action === 'finishTrial' || c.action === 'abort')
              state = {
                ...state,
                running: false,
                status: c.action === 'abort' ? '未完了' : '記録終了',
              };
            else if (c.action === 'trainingEnd') state = { ...state, trainingEnded: true };
            else if (c.action === 'setSelection') {
              const s = JSON.parse(c.value);
              state = {
                ...state,
                phase: [
                  'Preparation',
                  'Baseline',
                  'Training',
                  'ImmediateAssessment',
                  'Rest',
                  'RetentionAssessment',
                ][s.phase],
                task: ['None', 'Straight', 'FigureEight'][s.task],
                repetition: s.repetition,
              };
            } else if (c.action === 'alignDisplay') state = { ...state, displayAligned: true };
            else if (c.action === 'eyeCalibration')
              state = { ...state, eyeCalibrationConfirmed: true };
            else if (c.action === 'setGroup') state = { ...state, group: c.value };
            else if (c.action === 'setInput')
              state = {
                ...state,
                sourceKind:
                  c.value === 'synthetic'
                    ? 'synthetic_pose_fixture'
                    : 'actual_unavailable_in_preview',
              };
            state = { ...state, revision: state.revision + 1 };
          }
        }
      } catch {}
      await new Promise((a) => setTimeout(a, 150));
    }
  };
  void loop();
  return Object.assign(
    () => {
      running = false;
    },
    {
      getSnapshot: () => state,
      setSnapshot: (value: any) => {
        state = value;
      },
    },
  );
}
