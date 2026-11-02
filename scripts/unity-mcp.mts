import { execFileSync, spawn } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function syncUnityPackage(root: string, version: string) {
  if (!/^\d+\.\d+\.\d+$/.test(version))
    throw new Error(`MCPサーバーの安定版バージョンを確認できません: ${version}`);
  const path = resolve(root, 'wheelchair-sonification/Packages/manifest.json');
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  if (!manifest.dependencies?.['com.coplaydev.unity-mcp'])
    throw new Error('UnityプロジェクトにMCPパッケージの指定がありません。');
  const source = `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v${version}`;
  if (manifest.dependencies['com.coplaydev.unity-mcp'] === source) return false;
  manifest.dependencies['com.coplaydev.unity-mcp'] = source;
  writeFileSync(path, JSON.stringify(manifest, null, 2) + '\n');
  return true;
}

// MCPのINFOログから、このサーバーが作ったHTTPセッションだけを追跡する。
export function collectSessions(line: string, sessions: Set<string>) {
  const match = line.match(
    /mcp\.server\.streamable_http_manager - INFO - Created new transport with session ID: ([a-f0-9]{32})\b/,
  );
  if (match) sessions.add(match[1]);
}

export async function closeSessions(endpoint: string, sessions: Set<string>, timeoutMs = 2000) {
  const closed = new Set<string>();
  // 終了要求中にログへ現れたセッションも含める。無限に再接続するクライアントは待たない。
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const pending = [...sessions].filter((id) => !closed.has(id));
    if (!pending.length) return;
    for (const id of pending) closed.add(id);
    const results = await Promise.allSettled(
      pending.map(async (id) => {
        const response = await fetch(endpoint, {
          method: 'DELETE',
          headers: {
            'mcp-session-id': id,
            'mcp-protocol-version': '2025-03-26',
            Accept: 'application/json, text/event-stream',
          },
          signal: AbortSignal.timeout(Math.max(1, deadline - Date.now())),
        });
        await response.arrayBuffer();
        // 404はクライアント側が既に終了したセッション。
        if (response.status !== 200 && response.status !== 404)
          throw new Error(`MCPセッション終了に失敗しました（HTTP ${response.status}）。`);
      }),
    );
    const failure = results.find((result) => result.status === 'rejected');
    if (failure?.status === 'rejected') throw failure.reason;
  }
  if ([...sessions].some((id) => !closed.has(id)))
    throw new Error('MCPセッションの終了待ちがタイムアウトしました。');
}

export function startServer(command: string, args: string[], endpoint: string) {
  const child = spawn(command, args, { detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const sessions = new Set<string>();
  const remainders = new Map<unknown, string>();
  let requested: NodeJS.Signals | undefined;
  let stopping = false;
  let shutdownError = false;
  for (const [input, output] of [
    [child.stdout, process.stdout],
    [child.stderr, process.stderr],
  ] as const) {
    input.setEncoding('utf8');
    input.on('data', (chunk: string) => {
      output.write(chunk);
      const lines = ((remainders.get(input) ?? '') + chunk).split('\n');
      remainders.set(input, lines.pop()!);
      for (const line of lines) collectSessions(line, sessions);
    });
  }
  const stop = async (signal: NodeJS.Signals) => {
    if (stopping) {
      shutdownError = true;
      console.error('MCPの終了待ちを中断し、MCPサーバーを強制終了します。');
      if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
      return;
    }
    stopping = true;
    requested = signal;
    try {
      await closeSessions(endpoint, sessions);
    } catch (error) {
      shutdownError = true;
      console.error('MCPの終了処理に失敗しました:', error instanceof Error ? error.message : error);
    }
    // UvicornのSIGINT再送によるKeyboardInterruptを避け、通常の終了処理を待つ。
    if (child.exitCode === null && child.signalCode === null) child.kill('SIGTERM');
  };
  const interrupt = () => {
    void stop('SIGINT');
  };
  const terminate = () => {
    void stop('SIGTERM');
  };
  process.on('SIGINT', interrupt);
  process.on('SIGTERM', terminate);
  return new Promise<number>((resolveResult) => {
    child.on('error', (error) => {
      console.error(error.message);
      shutdownError = true;
    });
    child.on('close', (code, signal) => {
      process.off('SIGINT', interrupt);
      process.off('SIGTERM', terminate);
      if (shutdownError) resolveResult(code && code !== 0 ? code : 1);
      else if (requested && (code === 0 || signal === 'SIGTERM'))
        resolveResult(requested === 'SIGINT' ? 130 : 143);
      else resolveResult(code ?? (signal === 'SIGINT' ? 130 : signal === 'SIGTERM' ? 143 : 1));
    });
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv[2] === 'sync-package' && process.argv.length === 3) {
    const tools = JSON.parse(
      execFileSync('mise', ['ls', '--current', '--json'], { encoding: 'utf8' }),
    );
    const server = tools['pypi:mcpforunityserver']?.[0];
    if (!server?.installed) throw new Error('先にmise install --lockedを実行してください。');
    const root = fileURLToPath(new URL('..', import.meta.url));
    const changed = syncUnityPackage(root, server.version);
    console.log(
      `Unity MCP ${server.version}: ${changed ? 'パッケージ指定を更新しました' : '同じ版を指定済みです'}。`,
    );
  } else if (process.argv.length !== 2) {
    console.error('Usage: unity-mcp.mts [sync-package]');
    process.exitCode = 2;
  } else
    process.exitCode = await startServer(
      'mise',
      [
        'exec',
        '--',
        'mcp-for-unity',
        '--transport',
        'http',
        '--http-host',
        '127.0.0.1',
        '--http-port',
        '8080',
      ],
      'http://127.0.0.1:8080/mcp',
    );
}
