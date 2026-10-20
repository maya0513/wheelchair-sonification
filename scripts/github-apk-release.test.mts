import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { publishRelease, releaseRepository } from './github-apk-release.mts';

function fixture(t: { after: (fn: () => void) => void }, scenario = '') {
  const root = mkdtempSync(join(tmpdir(), 'apk publish test '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const store = join(root, 'local signing key'); writeFileSync(store, 'key');
  const env = { UNITY_ANDROID_KEYSTORE: store, UNITY_ANDROID_KEY_ALIAS: 'test',
    UNITY_ANDROID_KEYSTORE_PASSWORD: 'private-password', UNITY_ANDROID_KEY_ALIAS_PASSWORD: 'private-password' };
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
    if (args[0] === 'auth') return scenario === 'auth' ? { status: 1, stdout: '', stderr: 'not logged in' } : ok();
    if (args[0] === 'api') {
      if (text.includes('/commits/')) return scenario === 'unpushed' ? { status: 1, stdout: '', stderr: 'HTTP 404' } : ok(commit);
      if (scenario === 'existing') return ok('{}');
      if (scenario === 'network') return { status: 1, stdout: '', stderr: 'HTTP 503' };
      return { status: 1, stdout: '', stderr: 'HTTP 404' };
    }
    if (args[1] === 'upload' && scenario === 'upload') return { status: 1, stdout: '', stderr: 'upload failed' };
    if (text.includes('--json assets')) return ok(JSON.stringify({ assets:
      ['WheelchairSonification.apk', 'build-info.json', 'SHA256SUMS.txt'].map(name => ({ name,
        size: name === 'build-info.json' ? Buffer.byteLength(JSON.stringify(info)) : 3 })) }));
    if (text.includes('--json url')) return ok('https://github.com/' + releaseRepository + '/releases/tag/v0.2.0');
    return ok();
  };
  const output = join(root, 'output');
  const info = { gitCommit: commit, gitDirty: false, version: '0.2.0', versionCode: 2, targetDevice: 'VIVE Focus Vision',
    xrLoader: 'OpenXR', inputMode: 'simulated', validation: { signatureVerified: true, xrManifestVerified: true } };
  const build = async (_: string, environment: NodeJS.ProcessEnv) => {
    built = true;
    assert.equal(environment.UNITY_BUILD_VERSION, '0.2.0');
    assert.equal(environment.UNITY_ANDROID_VERSION_CODE, '2');
    if (scenario === 'build') throw new Error('build failed');
    mkdirSync(output);
    if (scenario === 'mismatched-commit') info.gitCommit = 'b'.repeat(40);
    writeFileSync(join(output, 'build-info.json'), JSON.stringify(info));
    for (const name of ['WheelchairSonification.apk', 'SHA256SUMS.txt']) writeFileSync(join(output, name), 'apk');
    return output;
  };
  return { root, env, run, build, calls, wasBuilt: () => built };
}

test('Release publishes only after fresh build and all three assets are uploaded', async t => {
  const f = fixture(t);
  const url = await publishRelease('v0.2.0', '2', f);
  assert.match(url, /releases\/tag\/v0.2.0$/);
  const operations = f.calls.filter(call => call[1] === 'release').map(call => call[2]);
  assert.deepEqual(operations, ['create', 'upload', 'view', 'edit', 'view']);
  assert.ok(f.calls.find(call => call[2] === 'create')!.includes('--draft'));
  assert.ok(f.calls.find(call => call[2] === 'edit')!.includes('--draft=false'));
  assert.ok(f.calls.every(call => !call.includes('--clobber')));
  assert.ok(!JSON.stringify(f.calls).includes('private-password'));
});

test('invalid version arguments, dirty source, unavailable commit and existing releases stop before building', async t => {
  for (const scenario of ['dirty', 'auth', 'unpushed', 'existing', 'network']) {
    const f = fixture(t, scenario);
    await assert.rejects(publishRelease('v0.2.0', '2', f));
    assert.equal(f.wasBuilt(), false);
    assert.ok(!f.calls.some(call => call[2] === 'create'));
  }
  const f = fixture(t);
  for (const [tag, code] of [['shell;injection', '2'], ['v0.2.0', '0'], ['v0.2.0', '2100000001']])
    await assert.rejects(publishRelease(tag, code, f));
  assert.equal(f.calls.length, 0);
});

test('failed or mismatched build never creates a Release', async t => {
  for (const scenario of ['build', 'mismatched-commit']) {
    const f = fixture(t, scenario);
    await assert.rejects(publishRelease('v0.2.0', '2', f));
    assert.ok(!f.calls.some(call => call[2] === 'create'));
  }
});

test('failed uploads leave the Release as a draft and report the recovery location', async t => {
  const f = fixture(t, 'upload');
  await assert.rejects(publishRelease('v0.2.0', '2', f), /Draft.*GitHub Releases/);
  assert.ok(f.calls.some(call => call[2] === 'create'));
  assert.ok(!f.calls.some(call => call[2] === 'edit'));
});
