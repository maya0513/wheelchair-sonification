// A PC-only monotonic stopwatch. No HMD clock or network timestamp enters its result.
export class TrialClock {
  constructor(now = () => performance.now()) { this.now = now; this.state = 'idle'; this.completedTrials = new Set(); }
  hasResultFor(trialId) { return !!trialId && this.completedTrials.has(trialId); }
  prepare(delayMs = 3000, trialId = '') {
    if (this.state === 'preparing' || this.state === 'running') throw new Error('既に計時中です。');
    if (this.hasResultFor(trialId)) throw new Error('この試行は計時済みです。再試行は新しい記録として開始してください。');
    this.trialId = trialId; this.startedAt = this.now() + delayMs; this.endedAt = null; this.state = 'preparing';
  }
  tick() { if (this.state === 'preparing' && this.now() >= this.startedAt) this.state = 'running'; return this.state; }
  stop() {
    this.tick(); if (this.state !== 'running') return null;
    this.endedAt = this.now(); this.state = 'stopped'; if (this.trialId) this.completedTrials.add(this.trialId); return this.result();
  }
  cancel(reason = 'operator_cancel') { const prior = this.state; this.state = 'cancelled'; return { ...this.result(), priorState: prior, reason }; }
  elapsed() { return this.startedAt === undefined ? 0 : Math.max(0, ((this.endedAt ?? this.now()) - this.startedAt) / 1000); }
  result() { return { trialId: this.trialId, state: this.state, startedAtMs: this.startedAt, endedAtMs: this.endedAt, seconds: this.endedAt == null ? null : this.elapsed(), clock: 'PC browser performance.now; audio scheduled, physical onset unmeasured' }; }
}
export function audioInterruptsClock(clockState, audioState) {
  return ['preparing', 'running'].includes(clockState) && audioState !== 'running';
}
export function spaceStops(event) {
  const tag = event.target?.tagName?.toLowerCase();
  return event.code === 'Space' && !event.repeat && !['input','textarea','select'].includes(tag) && !event.target?.isContentEditable;
}
