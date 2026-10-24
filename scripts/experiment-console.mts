import { createServer } from 'node:http';
import { createHash, randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { createReadStream, createWriteStream, existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { pipeline } from 'node:stream/promises';
import { dirname, join, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, spawnSync } from 'node:child_process';
import { networkInterfaces, homedir } from 'node:os';

export const consolePort = 8765;
const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const publicDirectory = join(here, 'console');
const equal = (a: string, b: string) => a.length === b.length && timingSafeEqual(Buffer.from(a), Buffer.from(b));
const hash = (text: string) => createHash('sha256').update(text).digest('hex');
const local = (address?: string) => address === '127.0.0.1' || address === '::1' || address === '::ffff:127.0.0.1';
export function accessPointAddresses() {
  const query = spawnSync('nmcli', ['-t', '-f', 'DEVICE,TYPE,STATE', 'device', 'status'], { encoding: 'utf8', shell: false });
  const addresses: string[] = [];
  if (query.status !== 0) return addresses;
  for (const line of query.stdout.split('\n')) {
    const [device, type, state] = line.split(':');
    if (type !== 'wifi' || state !== 'connected') continue;
    const mode = spawnSync('nmcli', ['-g', 'GENERAL.CONNECTION', 'device', 'show', device], { encoding: 'utf8', shell: false });
    if (mode.status !== 0 || !mode.stdout.trim()) continue;
    const detail = spawnSync('nmcli', ['-g', '802-11-wireless.mode', 'connection', 'show', mode.stdout.trim()], { encoding: 'utf8', shell: false });
    if (detail.status !== 0 || detail.stdout.trim() !== 'ap') continue;
    for (const item of networkInterfaces()[device] || []) if (item.family === 'IPv4' && !item.internal) addresses.push(item.address);
  }
  return addresses;
}

type Candidate = { deviceId: string; codeHash: string; name: string; seen: number; token?: string };
type Command = { id: string; visitId: string; revision: number; action: string; value: string; number: number; flag: boolean; created: number };
export function createConsoleServer(options: { dataDirectory?: string; demo?: boolean; now?: () => number; port?: number } = {}) {
  const now = options.now || Date.now;
  const port = options.port ?? consolePort;
  const directory = options.dataDirectory || join(homedir(), 'WheelchairSonificationData');
  const candidates = new Map<string, Candidate>();
  const csrf = randomBytes(32).toString('hex');
  const browserToken = randomBytes(32).toString('hex');
  let approved: Candidate | undefined, browserSeen = 0, stateSeen = 0, latestState: any = null;
  let pending: Command[] = [], result: any = null;
  const archives: { id: string; name: string; sha256: string; size: number }[] = [];
  const archiveIndex = join(directory, 'archives.json');
  if (existsSync(archiveIndex)) {
    try {
      const saved = JSON.parse(readFileSync(archiveIndex, 'utf8'));
      if (Array.isArray(saved)) for (const item of saved) {
        if (/^[a-f0-9-]{36}$/.test(item.id || '') && /^[a-f0-9-]{36}-Wheelchair[A-Za-z0-9_-]+\.zip$/.test(item.name || '') &&
            /^[a-f0-9]{64}$/.test(item.sha256 || '') && existsSync(join(directory, item.name)) && statSync(join(directory, item.name)).size === item.size)
          archives.push(item);
      }
    } catch { console.warn('既存の回収一覧を読めません。元のZIPは保持しています。'); }
  }
  const listeners: ReturnType<typeof createServer>[] = [];
  const send = (response: any, status: number, body: any) => {
    response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
    response.end(JSON.stringify(body));
  };
  async function json(request: any) {
    let size = 0; const chunks = [];
    for await (const chunk of request) { size += chunk.length; if (size > 128 * 1024) throw new Error('要求が大きすぎます。'); chunks.push(chunk); }
    return JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
  }
  function deviceAuthorized(request: any) { return !!approved && equal(request.headers['x-device-token'] || '', approved.token || 'unpaired'); }
  const activeOperator = () => now() - browserSeen < 3000 && browserSeen !== 0;
  function clearExpired() {
    if (!activeOperator()) pending = [];
    pending = pending.filter(c => now() - c.created < 3000);
  }
  function persistOperator(visitId: string, entry: any) {
    if (!/^[A-Za-z0-9_-]{1,64}$/.test(visitId) || !/^[A-Za-z0-9_-]{1,80}$/.test(entry?.id || '') || JSON.stringify(entry).length > 100000)
      throw new Error('記録形式が不正です。');
    const folder = join(directory, visitId); mkdirSync(folder, { recursive: true, mode: 0o700 });
    const file = join(folder, 'operator.json');
    const saved = existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : { format: 2, visitId, entries: [] };
    if (saved.format !== 2) throw new Error('旧形式の記録を上書きしません。');
    if (!saved.entries.some((item:any) => item.id === entry.id)) saved.entries.push({ ...entry, pcSavedUtc: new Date(now()).toISOString() });
    writeFileSync(file + '.partial', JSON.stringify(saved, null, 2) + '\n', { mode: 0o600 }); renameSync(file + '.partial', file);
    return { saved: true, file };
  }
  const handler = async (request: any, response: any) => {
    try {
      const url = new URL(request.url, 'http://127.0.0.1:' + port);
      const isLocal = local(request.socket.remoteAddress);
      if (url.pathname.startsWith('/device/')) {
        if (options.demo && !isLocal) return send(response, 403, { error: '模擬コンソールへ実機は接続できません。' });
        if (url.pathname === '/device/hello' && request.method === 'POST') {
          const data = await json(request);
          if (data.schema !== 2) return send(response, 409, { error: '通信形式が異なります。v0.4.0以降の対応するAPKとコンソールを組み合わせてください。' });
          if (!/^[a-f0-9]{32}$/.test(data.deviceId) || !/^[a-f0-9]{64}$/.test(data.codeHash)) return send(response, 400, { error: '接続識別が不正です。' });
          let candidate = candidates.get(data.deviceId);
          if (!candidate || candidate.codeHash !== data.codeHash) {
            candidate = { deviceId: data.deviceId, codeHash: data.codeHash, name: 'Focus Vision', seen: now() };
            candidates.set(data.deviceId, candidate);
          }
          candidate.seen = now();
          return send(response, 200, { schema: 2, pairToken: approved === candidate ? candidate.token : '', operatorConnected: activeOperator(), operatorLeaseSeconds: Math.max(0, (3000 - (now() - browserSeen)) / 1000), demo: !!options.demo });
        }
        if (!deviceAuthorized(request)) return send(response, 401, { error: '装着前にPCで接続承認してください。' });
        if (url.pathname === '/device/exchange' && request.method === 'POST') {
          const body = await json(request);
          if (body.state?.schema !== 2) return send(response, 409, { error: '旧アプリの通信形式を拒否しました。' });
          latestState = body.state; stateSeen = now();
          if (body.result?.id) {
            const command = pending.find(c => c.id === body.result.id);
            if (command && body.state?.visitId) persistOperator(body.state.visitId, { id: command.id, type: 'operator_command', trialId: body.state.trialId || '', action: command.action, value: command.value, number: command.number, requestedPcUtc: new Date(command.created).toISOString(), result: body.result });
            result = body.result; pending = pending.filter(c => c.id !== body.result.id);
          }
          clearExpired();
          return send(response, 200, { schema: 2, operatorConnected: activeOperator(), operatorLeaseSeconds: Math.max(0, (3000 - (now() - browserSeen)) / 1000), command: pending[0] || null });
        }
        if (url.pathname === '/device/export' && request.method === 'PUT') {
          if (!activeOperator() || latestState?.running) return send(response, 409, { error: '停止中に取り出してください。' });
          const expected = request.headers['x-content-sha256'];
          const name = String(request.headers['x-export-name'] || '');
          if (!/^[a-f0-9]{64}$/.test(expected || '') || !/^Wheelchair[A-Za-z0-9_-]+\.zip$/.test(name)) return send(response, 400, { error: '取り出し情報が不正です。' });
          mkdirSync(directory, { recursive: true, mode: 0o700 });
          const id = randomUUID(); const file = join(directory, id + '-' + name), temporary = file + '.partial';
          const digest = createHash('sha256'); let size = 0;
          request.on('data', (chunk: Buffer) => { size += chunk.length; digest.update(chunk); });
          try {
            await pipeline(request, createWriteStream(temporary, { flags: 'wx', mode: 0o600 }));
            const actual = digest.digest('hex');
            if (actual !== expected || size === 0) throw new Error('チェックサムが一致しません。');
            renameSync(temporary, file); archives.push({ id, name: basename(file), sha256: actual, size });
            writeFileSync(file + '.sha256', actual + '  ' + basename(file) + '\n', { mode: 0o600 });
            writeFileSync(archiveIndex + '.partial', JSON.stringify(archives, null, 2) + '\n', { mode: 0o600 });
            renameSync(archiveIndex + '.partial', archiveIndex);
            return send(response, 200, { saved: true, sha256: actual, size });
          } catch (error) { rmSync(temporary, { force: true }); throw error; }
        }
        return send(response, 404, { error: '未対応の機器要求です。' });
      }
      if (!isLocal) return send(response, 403, { error: '操作画面はこのPCのブラウザー専用です。' });
      if (request.method !== 'GET' && request.headers.origin && request.headers.origin !== 'http://127.0.0.1:' + port)
        return send(response, 403, { error: '異なるサイトからの操作を拒否しました。' });
      if (url.pathname === '/' && request.method === 'GET') {
        const page = readFileSync(join(publicDirectory, 'index.html'), 'utf8').replace('__CSRF__', csrf).replace('__DEMO__', options.demo ? 'true' : 'false');
        response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store',
          'Set-Cookie': 'operator=' + browserToken + '; HttpOnly; SameSite=Strict; Path=/',
          'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'" }); response.end(page); return;
      }
      if (['/console.js','/console.css','/timing.mjs'].includes(url.pathname)) {
        response.writeHead(200, { 'Content-Type': !url.pathname.endsWith('.css') ? 'text/javascript; charset=utf-8' : 'text/css; charset=utf-8' });
        response.end(readFileSync(join(publicDirectory, url.pathname.slice(1)))); return;
      }
      if (!String(request.headers.cookie || '').split(';').some((s: string) => s.trim() === 'operator=' + browserToken)) return send(response, 401, { error: '操作ページを開き直してください。' });
      if (request.method === 'POST' && !equal(request.headers['x-csrf-token'] || '', csrf)) return send(response, 403, { error: '操作認証に失敗しました。' });
      if (url.pathname === '/api/heartbeat' && request.method === 'POST') { browserSeen = now(); return send(response, 200, { ok: true }); }
      if (url.pathname === '/api/state' && request.method === 'GET') {
        clearExpired(); return send(response, 200, { demo: !!options.demo, connected: !!approved && now() - stateSeen < 3000,
          staleMs: stateSeen ? now() - stateSeen : null, operatorConnected: activeOperator(), state: latestState, result,
          candidates: [...candidates.values()].filter(c => now() - c.seen < 10000).map(c => ({ deviceId: c.deviceId, name: c.name })),
          paired: !!approved, pending: pending.length, archives, dataDirectory: directory });
      }
      if (url.pathname === '/api/pair' && request.method === 'POST') {
        const data = await json(request), candidate = candidates.get(data.deviceId);
        if (approved && approved.deviceId !== data.deviceId) return send(response, 409, { error: '別の機器が承認済みです。変更はコンソールを再起動してください。' });
        if (!candidate || now() - candidate.seen > 10000 || !/^\d{6}$/.test(data.code || '') || !equal(hash(data.deviceId + ':' + data.code), candidate.codeHash))
          return send(response, 400, { error: 'ゴーグルに表示された6桁の番号を確認してください。' });
        approved = candidate; approved.token ||= randomBytes(32).toString('hex');
        return send(response, 200, { approved: true });
      }
      if (url.pathname === '/api/record' && request.method === 'POST') {
        const body = await json(request);
        return send(response, 200, persistOperator(body.visitId, body.entry));
      }
      if (url.pathname === '/api/command' && request.method === 'POST') {
        if (!approved || !activeOperator() || now() - stateSeen >= 3000) return send(response, 409, { error: '最新の接続状態を確認できません。' });
        const body = await json(request);
        const allowed = ['createVisit','setInput','setGroup','setSelection','start','finishTrial','trainingEnd','abort','assignRole','calibrate','resetCalibration','setVolume','export','exportUsb','quick'];
        if (!allowed.includes(body.action) || typeof body.visitId !== 'string' || !Number.isInteger(body.revision)) return send(response, 400, { error: '操作形式が不正です。' });
        const command: Command = { id: randomUUID(), visitId: body.visitId, revision: body.revision, action: body.action,
          value: String(body.value || '').slice(0, 4000), number: Number.isFinite(body.number) ? body.number : 0, flag: body.flag === true, created: now() };
        if (command.action === 'abort') pending = [command];
        else if (pending.length) return send(response, 409, { error: '前の操作の応答を待っています。' });
        else pending.push(command);
        return send(response, 202, { id: command.id });
      }
      if (url.pathname.startsWith('/api/archive/') && request.method === 'GET') {
        const archive = archives.find(a => a.id === url.pathname.slice('/api/archive/'.length));
        if (!archive) return send(response, 404, { error: '回収した記録がありません。' });
        const file = join(directory, archive.name);
        response.writeHead(200, { 'Content-Type': 'application/zip', 'Content-Length': statSync(file).size,
          'Content-Disposition': 'attachment; filename="' + archive.name + '"', 'Cache-Control': 'no-store' });
        createReadStream(file).pipe(response); return;
      }
      return send(response, 404, { error: 'ページがありません。' });
    } catch (error) { if (!response.headersSent) send(response, 400, { error: error instanceof Error ? error.message : '処理に失敗しました。' }); else response.destroy(); }
  };
  return {
    listen: async (address = '127.0.0.1') => {
      const server = createServer(handler); server.requestTimeout = 120000; server.headersTimeout = 15000;
      await new Promise<void>((accept, reject) => { server.once('error', reject); server.listen(port, address, accept); });
      listeners.push(server); return server.address();
    },
    close: async () => { await Promise.all(listeners.map(s => new Promise<void>(accept => { s.closeAllConnections(); s.close(() => accept()); }))); },
    options, directory,
  };
}

export async function startConsole(demo = false) {
  const app = createConsoleServer({ demo });
  await app.listen();
  const aps = demo ? [] : accessPointAddresses();
  for (const address of aps) await app.listen(address);
  console.log('実施者用画面: http://127.0.0.1:' + consolePort + '/');
  console.log(demo ? '模擬モード：実機計測には使用できません。' : aps.length ? '接続済みホットスポット内だけで機器通信を受け付けます。' : 'ホットスポットは未接続です。Wi-Fi設定で有効にした後、このコマンドを起動し直してください。設定は変更していません。');
  if (demo) {
    const { startDemoDevice } = await import('./console-demo.mts');
    const stop = await startDemoDevice('http://127.0.0.1:' + consolePort);
    process.once('SIGINT', stop); process.once('SIGTERM', stop);
  }
  if (process.env.WHEELCHAIR_NO_BROWSER !== '1') spawn(process.platform === 'win32' ? 'cmd.exe' : 'xdg-open',
    process.platform === 'win32' ? ['/c', 'start', '', 'http://127.0.0.1:' + consolePort] : ['http://127.0.0.1:' + consolePort], { stdio: 'ignore', detached: true }).unref();
  const stop = async () => { await app.close(); process.exitCode = 0; };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
  return app;
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  startConsole(process.argv.includes('--demo')).catch(error => { console.error(error.message); process.exitCode = 1; });
}
