import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { createHash } from 'node:crypto';
import { publishRelease, releaseRepository } from './github-apk-release.mts';

function fixture(t: { after: (fn: () => void) => void }, scenario = '') {
  const root = mkdtempSync(join(tmpdir(), 'apk publish test '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const store = join(root, 'local signing key');
  writeFileSync(store, 'key');
  const env = {
    UNITY_ANDROID_KEYSTORE: store,
    UNITY_ANDROID_KEY_ALIAS: 'test',
    UNITY_ANDROID_KEYSTORE_PASSWORD: 'private-password',
    UNITY_ANDROID_KEY_ALIAS_PASSWORD: 'private-password',
  };
  const calls: string[][] = [];
  let built = false;
  const commit = 'a'.repeat(40);
  const run = (command: string, args: string[]) => {
    calls.push([command, ...args]);
    const text = args.join(' ');
    const ok = (stdout = '') => ({ status: 0, stdout, stderr: '' });
    if (command === 'git') {
      if (args[0] === 'remote') return ok('https://github.com/' + releaseRepository + '.git');
      if (args[0] === 'rev-parse') return ok(commit);
      if (args[0] === 'status') return ok(scenario === 'dirty' ? ' M source.cs' : '');
    }
    if (args[0] === 'auth')
      return scenario === 'auth' ? { status: 1, stdout: '', stderr: 'not logged in' } : ok();
    if (args[0] === 'api') {
      if (text.includes('/commits/'))
        return scenario === 'unpushed' ? { status: 1, stdout: '', stderr: 'HTTP 404' } : ok(commit);
      if (scenario === 'existing') return ok('{}');
      if (scenario === 'network') return { status: 1, stdout: '', stderr: 'HTTP 503' };
      return { status: 1, stdout: '', stderr: 'HTTP 404' };
    }
    if (args[1] === 'upload' && scenario === 'upload')
      return { status: 1, stdout: '', stderr: 'upload failed' };
    if (text.includes('--json assets'))
      return ok(
        JSON.stringify({
          assets: [
            {
              name: 'WheelchairSonification.apk',
              size: statSync(join(output, 'WheelchairSonification.apk')).size,
            },
            ...(scenario === 'extra-asset' ? [{ name: 'unexpected.zip', size: 1 }] : []),
          ],
        }),
      );
    if (text.includes('--json url'))
      return ok('https://github.com/' + releaseRepository + '/releases/tag/v0.2.0');
    return ok();
  };
  const output = join(root, 'output');
  const info = {
    gitCommit: commit,
    gitDirty: false,
    version: '0.2.0',
    versionCode: 2,
    sha256: createHash('sha256').update('apk').digest('hex'),
    targetDevice: 'VIVE Focus Vision',
    operatorProtocol: 3,
    headsetUi: 'guided-preparation-and-participant',
    androidTargetSdk: 36,
    xrLoader: 'OpenXR',
    inputMode: 'simulated-and-vive-ultimate-tracker',
    validation: { signatureVerified: true, xrManifestVerified: true },
  };
  const build = async (_: string, environment: NodeJS.ProcessEnv) => {
    built = true;
    assert.equal(environment.UNITY_BUILD_VERSION, '0.2.0');
    assert.equal(environment.UNITY_ANDROID_VERSION_CODE, '2');
    if (scenario === 'build') throw new Error('build failed');
    mkdirSync(output);
    if (scenario === 'mismatched-commit') info.gitCommit = 'b'.repeat(40);
    writeFileSync(join(output, 'build-info.json'), JSON.stringify(info));
    for (const name of ['WheelchairSonification.apk', 'SHA256SUMS.txt'])
      writeFileSync(join(output, name), 'apk');
    if (scenario === 'tampered-apk')
      writeFileSync(join(output, 'WheelchairSonification.apk'), 'changed');
    return output;
  };

  return { root, env, run, build, calls, wasBuilt: () => built };
}

test('Release publishes a freshly built APK as the only attached asset', async (t) => {
  const f = fixture(t);
  const url = await publishRelease('v0.2.0', '2', f);
  assert.match(url, /releases\/tag\/v0.2.0$/);
  const operations = f.calls.filter((call) => call[1] === 'release').map((call) => call[2]);
  assert.deepEqual(operations, ['create', 'upload', 'view', 'edit', 'view']);
  assert.ok(f.calls.find((call) => call[2] === 'create')!.includes('--draft'));
  assert.ok(f.calls.find((call) => call[2] === 'edit')!.includes('--draft=false'));
  assert.ok(f.calls.every((call) => !call.includes('--clobber')));
  assert.ok(!JSON.stringify(f.calls).includes('private-password'));
  const upload = f.calls.find((call) => call[1] === 'release' && call[2] === 'upload')!;
  assert.equal(upload.filter((argument) => argument.startsWith(join(f.root, 'output'))).length, 1);
  assert.ok(upload.includes(join(f.root, 'output/WheelchairSonification.apk')));
  assert.match(readFileSync(join(f.root, 'output/release-notes.md'), 'utf8'), /画像付きの説明/);
});

test('invalid version arguments, dirty source, unavailable commit and existing releases stop before building', async (t) => {
  for (const scenario of ['dirty', 'auth', 'unpushed', 'existing', 'network']) {
    const f = fixture(t, scenario);
    await assert.rejects(publishRelease('v0.2.0', '2', f));
    assert.equal(f.wasBuilt(), false);
    assert.ok(!f.calls.some((call) => call[2] === 'create'));
  }
  const f = fixture(t);
  for (const [tag, code] of [
    ['shell;injection', '2'],
    ['v0.2.0', '0'],
    ['v0.2.0', '2100000001'],
  ])
    await assert.rejects(publishRelease(tag, code, f));
  assert.equal(f.calls.length, 0);
});

test('failed or mismatched build never creates a Release', async (t) => {
  for (const scenario of ['build', 'mismatched-commit', 'tampered-apk']) {
    const f = fixture(t, scenario);
    await assert.rejects(publishRelease('v0.2.0', '2', f));
    assert.ok(!f.calls.some((call) => call[2] === 'create'));
  }
});

test('failed uploads leave the Release as a draft and report the recovery location', async (t) => {
  const f = fixture(t, 'upload');
  await assert.rejects(publishRelease('v0.2.0', '2', f), /Draft.*GitHub Releases/);
  assert.ok(f.calls.some((call) => call[2] === 'create'));
  assert.ok(!f.calls.some((call) => call[2] === 'edit'));
});

test('an unexpected extra asset leaves the release unpublished', async (t) => {
  const f = fixture(t, 'extra-asset');
  await assert.rejects(publishRelease('v0.2.0', '2', f), /APK一件/);
  assert.ok(!f.calls.some((call) => call[2] === 'edit'));
});
