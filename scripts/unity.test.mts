import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const script = join(dirname(fileURLToPath(import.meta.url)), 'unity.sh');
const successLog = 'Exiting batchmode successfully now!\nApplication will exit with return code 0\n';

function fixture(t: { after: (fn: () => void) => void }) {
  const root = mkdtempSync(join(tmpdir(), 'unity test '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  for (const dir of ['scripts', 'bin', 'wheelchair-sonification/ProjectSettings', 'wheelchair-sonification/Temp']) mkdirSync(join(root, dir), { recursive: true });
  copyFileSync(script, join(root, 'scripts/unity.sh'));
  writeFileSync(join(root, 'wheelchair-sonification/ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 6000.6.4f1\r\n');
  writeFileSync(join(root, 'bin/ldd'), '#!/bin/sh\nif [ "$TEST_CASE" = libraries ]; then echo "libmissing.so => not found"; else echo "共有ライブラリOK"; fi\n', { mode: 0o755 });
  const editor = join(root, 'fake Unity');
  writeFileSync(editor, `#!/bin/bash
set -eu
printf '%s\\n' "$@" > "${root}/invocation"
log=''
while [[ $# -gt 0 ]]; do
  if [[ "$1" == -logFile ]]; then log="$2"; shift; fi
  shift
done
case "$TEST_CASE" in
  no-log) exit 0 ;;
  failure) echo 'license failed' >&2; exit 7 ;;
  compiler) printf 'error CS1002: expected ;\\n${successLog}' > "$log" ;;
  incomplete) echo 'startup only' > "$log" ;;
  *) printf '${successLog}' > "$log" ;;
esac
`, { mode: 0o755 });
  const execute = (mode = 'check', scenario = 'success', args: string[] = [], graphicsApi = '') => spawnSync('bash', [join(root, 'scripts/unity.sh'), mode, ...args], {
    cwd: tmpdir(), encoding: 'utf8', timeout: 5000,
    env: { ...process.env, UNITY_EDITOR: editor, UNITY_GRAPHICS_API: graphicsApi, TEST_CASE: scenario, PATH: join(root, 'bin') + ':' + process.env.PATH },
  });
  return { root, execute };
}

test('invalid arguments are rejected before looking for an Editor', () => {
  for (const args of [[], ['unknown'], ['path', 'extra']]) {
    const result = spawnSync('bash', [script, ...args], { encoding: 'utf8', env: { ...process.env, UNITY_EDITOR: '/missing' } });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Usage:/);
  }
});

test('batch success needs a new log with normal termination', t => {
  const { root, execute } = fixture(t);
  const result = execute();
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /依存解決・コンパイル成功/);
  assert.match(readFileSync(join(root, 'invocation'), 'utf8'), new RegExp('-projectPath\\n' + root + '/wheelchair-sonification\\n'));
});

test('open and check refuse a locked project without launching', t => {
  const { root, execute } = fixture(t);
  writeFileSync(join(root, 'wheelchair-sonification/Temp/UnityLockfile'), '');
  for (const mode of ['open', 'check']) {
    const result = execute(mode);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Editorを閉じて/);
    assert.equal(existsSync(join(root, 'invocation')), false);
  }
});

test('missing libraries prevent startup', t => {
  const { root, execute } = fixture(t);
  const result = execute('open', 'libraries');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /libmissing.so => not found/);
  assert.equal(existsSync(join(root, 'invocation')), false);
});

test('GUI graphics API override selects exactly the requested argument', t => {
  const { root, execute } = fixture(t);
  for (const [api, flag] of [['vulkan', '-force-vulkan'], ['opengl', '-force-glcore']]) {
    const result = execute('open', 'success', [], api);
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(join(root, 'invocation'), 'utf8').trim().split('\n');
    assert.equal(args[0], flag);
    assert.equal(args.filter(arg => arg.startsWith('-force-')).length, 1);
  }
});

test('GUI defaults to Vulkan', t => {
  const { root, execute } = fixture(t);
  const result = execute('open');
  assert.equal(result.status, 0, result.stderr);
  assert.equal(readFileSync(join(root, 'invocation'), 'utf8').split('\n')[0], '-force-vulkan');
});

test('invalid GUI graphics API is rejected before looking for an Editor', () => {
  const result = spawnSync('bash', [script, 'open'], {
    encoding: 'utf8', env: { ...process.env, UNITY_EDITOR: '/missing', UNITY_GRAPHICS_API: 'unsupported' },
  });
  assert.equal(result.status, 2);
  assert.match(result.stderr, /UNITY_GRAPHICS_API/);
});

test('non-graphical batch verification ignores the GUI graphics API', t => {
  const { root, execute } = fixture(t);
  const result = execute('check', 'success', [], 'unsupported');
  assert.equal(result.status, 0, result.stderr);
  const args = readFileSync(join(root, 'invocation'), 'utf8').trim().split('\n');
  assert.ok(args.includes('-nographics'));
  assert.ok(args.every(arg => !arg.startsWith('-force-')));
});

test('failed Editor exit code survives when no log is generated', t => {
  const { execute } = fixture(t);
  const result = execute('check', 'failure');
  assert.equal(result.status, 7);
  assert.match(result.stderr, /終了コード: 7/);
  assert.match(result.stderr, /今回のログは生成されませんでした/);
});

test('an old successful log cannot make a log-less run succeed', t => {
  const { root, execute } = fixture(t);
  const logs = join(root, '.local/logs');
  mkdirSync(logs, { recursive: true });
  writeFileSync(join(logs, 'unity-check.log'), successLog);
  const result = execute('check', 'no-log');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /今回のログがありません/);
  assert.equal(readFileSync(join(logs, 'unity-check.log.previous'), 'utf8'), successLog);
});

test('compiler errors fail even with exit code zero', t => {
  const { execute } = fixture(t);
  const result = execute('check', 'compiler');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /コンパイルエラー/);
});

test('incomplete log cannot be treated as success', t => {
  const { execute } = fixture(t);
  const result = execute('check', 'incomplete');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /正常終了記録がありません/);
});
