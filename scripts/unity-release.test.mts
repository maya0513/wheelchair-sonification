import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { apkName, buildRelease, editorPath } from './unity-release.mts';

function fixture(t: { after: (cleanup: () => void) => void }) {
  const root = mkdtempSync(join(tmpdir(), 'unity release with spaces & '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, 'wheelchair-sonification/ProjectSettings'), { recursive: true });
  writeFileSync(join(root, 'wheelchair-sonification/ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 6000.6.4f1\r\n');
  const settings = join(root, 'wheelchair-sonification/ProjectSettings/ProjectSettings.asset');
  writeFileSync(settings, 'original settings\r\n');
  const renderSettings = ['Mobile_RPAsset.asset', 'UniversalRenderPipelineGlobalSettings.asset']
    .map(name => join(root, 'wheelchair-sonification/Assets/Settings', name));
  mkdirSync(join(root, 'wheelchair-sonification/Assets/Settings'), { recursive: true });
  for (const path of renderSettings) writeFileSync(path, 'original render settings\r\n');
  const store = join(root, 'private signing key.jks');
  writeFileSync(store, 'fixture key');
  const env = { ...process.env, UNITY_EDITOR: process.execPath, UNITY_ANDROID_KEYSTORE: store,
    UNITY_ANDROID_KEY_ALIAS: 'wheelchair', UNITY_ANDROID_KEYSTORE_PASSWORD: 'do-not-log-this-store-secret',
    UNITY_ANDROID_KEY_ALIAS_PASSWORD: 'do-not-log-this-alias-secret' };
  delete env.UNITY_ANDROID_VERSION_CODE;
  const output = join(root, '.local/builds/android-release');
  mkdirSync(output, { recursive: true });
  writeFileSync(join(output, apkName), 'previous APK');
  const log = join(root, '.local/logs/unity-release-android.log');
  const complete = (request: { args: string[]; env: NodeJS.ProcessEnv }) => {
    writeFileSync(settings, 'Unity changed build settings\n');
    for (const path of renderSettings) writeFileSync(path, 'Unity changed render settings\n');
    const staging = request.env.WHEELCHAIR_BUILD_DIRECTORY!;
    writeFileSync(log, 'Android release built: ' + staging);
    writeFileSync(join(staging, apkName), 'new APK');
    writeFileSync(join(staging, 'build-info.json'), JSON.stringify({ result: 'Succeeded', target: 'Android',
      unityVersion: '6000.6.4f1', development: false, customSigning: true, architecture: 'ARM64', scriptingBackend: 'IL2CPP' }));
  };
  return { root, env, output, log, settings, renderSettings, complete };
}

test('Editor locations follow the OS and the exact project version', () => {
  assert.equal(editorPath('6000.6.4f1', 'win32', { ProgramFiles: 'D:\\Program Files' }),
    'D:\\Program Files\\Unity\\Hub\\Editor\\6000.6.4f1\\Editor\\Unity.exe');
  assert.equal(editorPath('6000.6.4f1', 'linux', {}, '/home/student'), '/home/student/Unity/Hub/Editor/6000.6.4f1/Editor/Unity');
  assert.equal(editorPath('6000.6.4f1', 'darwin', {}), '/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity');
});

test('signing must be configured before launching Unity and errors omit secrets', async t => {
  const f = fixture(t);
  delete f.env.UNITY_ANDROID_KEY_ALIAS;
  await assert.rejects(buildRelease(f.root, f.env, async () => assert.fail('Unity must not launch')), error => {
    assert.match(String(error), /UNITY_ANDROID_KEY_ALIAS/);
    assert.doesNotMatch(String(error), /do-not-log-this/);
    return true;
  });
});

test('an open project or another release build prevents launching Unity', async t => {
  const f = fixture(t);
  const projectLock = join(f.root, 'wheelchair-sonification/Temp/UnityLockfile');
  mkdirSync(join(f.root, 'wheelchair-sonification/Temp'));
  writeFileSync(projectLock, '');
  const neverLaunch = async () => assert.fail('Unity must not launch');
  await assert.rejects(buildRelease(f.root, f.env, neverLaunch), /Editorを閉じて/);
  rmSync(projectLock);
  writeFileSync(join(f.root, '.local/builds/.android-release.lock'), '');
  await assert.rejects(buildRelease(f.root, f.env, neverLaunch), /別のAPKビルド/);
});

test('a successful new APK is promoted with a matching hash and metadata', async t => {
  const f = fixture(t);
  await buildRelease(f.root, f.env, async request => {
    assert.equal(request.cwd, f.root);
    assert.equal(request.args[request.args.indexOf('-projectPath') + 1], join(f.root, 'wheelchair-sonification'));
    assert.equal(request.args[request.args.indexOf('-buildTarget') + 1], 'Android');
    assert.equal(request.args[request.args.indexOf('-executeMethod') + 1], 'WheelchairSonification.Editor.ReleaseBuildTools.BuildAndroid');
    assert.ok(request.args.includes('-quit'));
    assert.ok(!request.args.some(arg => /do-not-log-this/.test(arg)));
    f.complete(request);
    return 0;
  });
  const info = JSON.parse(readFileSync(join(f.output, 'build-info.json'), 'utf8'));
  const hash = createHash('sha256').update('new APK').digest('hex');
  assert.equal(info.sha256, hash);
  assert.equal(info.apk, apkName);
  assert.equal(readFileSync(join(f.output, 'SHA256SUMS.txt'), 'utf8'), `${hash}  ${apkName}\n`);
  assert.equal(readFileSync(join(f.output + '.previous', apkName), 'utf8'), 'previous APK');
  assert.equal(readFileSync(f.settings, 'utf8'), 'original settings\r\n');
  for (const path of f.renderSettings) assert.equal(readFileSync(path, 'utf8'), 'original render settings\r\n');
  assert.deepEqual(readdirSync(join(f.root, '.local/builds')).sort(), ['android-release', 'android-release.previous']);
});

test('failed Unity preserves the previous APK and removes incomplete output and lock', async t => {
  const f = fixture(t);
  await assert.rejects(buildRelease(f.root, f.env, async () => {
    writeFileSync(f.settings, 'Unity changed build settings\n');
    for (const path of f.renderSettings) writeFileSync(path, 'Unity changed render settings\n');
    return 7;
  }), /終了コード7/);
  assert.equal(readFileSync(join(f.output, apkName), 'utf8'), 'previous APK');
  assert.equal(readFileSync(f.settings, 'utf8'), 'original settings\r\n');
  for (const path of f.renderSettings) assert.equal(readFileSync(path, 'utf8'), 'original render settings\r\n');
  assert.deepEqual(readdirSync(join(f.root, '.local/builds')), ['android-release']);
});

test('a zero exit and an old success cannot mask missing or invalid current output', async t => {
  for (const scenario of ['no-log', 'no-info', 'empty-apk', 'development', 'wrong-version', 'no-signing', 'compiler']) {
    await t.test(scenario, async t => {
      const f = fixture(t);
      mkdirSync(join(f.root, '.local/logs'), { recursive: true });
      writeFileSync(f.log, 'old successful log');
      await assert.rejects(buildRelease(f.root, f.env, async request => {
        f.complete(request);
        const directory = request.env.WHEELCHAIR_BUILD_DIRECTORY!;
        if (scenario === 'no-log') rmSync(f.log);
        if (scenario === 'no-info') rmSync(join(directory, 'build-info.json'));
        if (scenario === 'empty-apk') writeFileSync(join(directory, apkName), '');
        if (scenario === 'compiler') writeFileSync(f.log, 'error CS1002: expected ;');
        const infoPath = join(directory, 'build-info.json');
        if (existsSync(infoPath)) {
          const info = JSON.parse(readFileSync(infoPath, 'utf8'));
          if (scenario === 'development') info.development = true;
          if (scenario === 'wrong-version') info.unityVersion = '6000.6.0f1';
          if (scenario === 'no-signing') info.customSigning = false;
          writeFileSync(infoPath, JSON.stringify(info));
        }
        return 0;
      }));
      assert.equal(readFileSync(join(f.output, apkName), 'utf8'), 'previous APK');
      assert.equal(readFileSync(f.log + '.previous', 'utf8'), 'old successful log');
      assert.equal(readFileSync(f.settings, 'utf8'), 'original settings\r\n');
      for (const path of f.renderSettings) assert.equal(readFileSync(path, 'utf8'), 'original render settings\r\n');
      assert.deepEqual(readdirSync(join(f.root, '.local/builds')), ['android-release']);
    });
  }
});

test('invalid Android version codes are rejected before launching Unity', async t => {
  const f = fixture(t);
  for (const code of ['0', '-1', '1.5', '2100000001', 'abc']) {
    await assert.rejects(buildRelease(f.root, { ...f.env, UNITY_ANDROID_VERSION_CODE: code }, async () => assert.fail('Unity must not launch')),
      /UNITY_ANDROID_VERSION_CODE/);
  }
});
