import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { prepareXrSdk, viveSdk } from './unity-xr-sdk.mts';
import { signingEnvironment, initializeSigning } from './android-signing.mts';
import { checkApkReports } from './android-apk-validation.mts';

function fixture(t: { after: (fn: () => void) => void }) {
  const root = mkdtempSync(join(tmpdir(), 'focus vision test '));
  t.after(() => rmSync(root, { force: true, recursive: true }));
  const bytes = 'official archive fixture';
  const spec = { ...viveSdk, sha256: createHash('sha256').update(bytes).digest('hex') };
  const directory = join(root, '.local/unity-packages');
  mkdirSync(directory, { recursive: true });
  return { root, bytes, spec, directory, path: join(directory, spec.name) };
}

test('SDK preparation downloads once, validates the fixed hash and reuses the cache', async t => {
  const f = fixture(t); let requests = 0;
  const fetcher = async () => { requests++; return new Response(f.bytes); };
  await prepareXrSdk(f.root, fetcher, f.spec);
  await prepareXrSdk(f.root, fetcher, f.spec);
  assert.equal(requests, 1);
  assert.equal(readFileSync(f.path, 'utf8'), f.bytes);
  writeFileSync(f.path, 'corrupted cache');
  await prepareXrSdk(f.root, fetcher, f.spec);
  assert.equal(requests, 2);
  assert.deepEqual(readdirSync(f.directory), [f.spec.name]);
});

test('SDK HTTP errors and incorrect archives are never promoted; locks are cleared', async t => {
  const f = fixture(t);
  for (const response of [new Response('failed', { status: 503 }), new Response('LFS pointer or invalid archive')]) {
    await assert.rejects(prepareXrSdk(f.root, async () => response, f.spec), /取得に失敗|SHA-256/);
    assert.equal(existsSync(f.path), false);
    assert.deepEqual(readdirSync(f.directory), []);
  }
  writeFileSync(f.path + '.lock', '');
  await assert.rejects(prepareXrSdk(f.root, async () => assert.fail('must not fetch'), f.spec), /別の処理/);
});

test('local signing configuration does not override any explicit environment profile', t => {
  const f = fixture(t);
  const values = { UNITY_ANDROID_KEYSTORE: '/fixture/key', UNITY_ANDROID_KEY_ALIAS: 'wheelchair',
    UNITY_ANDROID_KEYSTORE_PASSWORD: 'private-store-password', UNITY_ANDROID_KEY_ALIAS_PASSWORD: 'private-alias-password' };
  writeFileSync(join(f.root, '.local/android-signing.json'), JSON.stringify(values));
  assert.equal(signingEnvironment(f.root, {}).UNITY_ANDROID_KEY_ALIAS, 'wheelchair');
  const explicit = signingEnvironment(f.root, { UNITY_ANDROID_KEY_ALIAS: 'different-key' });
  assert.equal(explicit.UNITY_ANDROID_KEY_ALIAS, 'different-key');
  assert.equal(explicit.UNITY_ANDROID_KEYSTORE_PASSWORD, undefined);
  writeFileSync(join(f.root, '.local/android-signing.json'), 'broken');
  assert.throws(() => signingEnvironment(f.root, {}), /署名設定を読めません/);
});

test('signing initialization refuses to replace an existing key profile', t => {
  const f = fixture(t);
  writeFileSync(join(f.root, '.local/android-signing.json'), '{}');
  assert.throws(() => initializeSigning(f.root, '/missing/editor', {}, () => assert.fail('no keytool')), /既にあります/);
});

const badging = "package: name='io.github.maya0513.wheelchairsonification' versionCode='2' versionName='0.2.0'\nnative-code: 'arm64-v8a'\n";
const manifest = 'org.khronos.openxr.intent.category.IMMERSIVE_HMD org.khronos.openxr.permission.OPENXR com.htc.ViveOpenXR.SdkVersion';
const entries = ['lib/arm64-v8a/libUnityOpenXR.so', 'lib/arm64-v8a/libopenxr_loader.so', 'lib/arm64-v8a/libviveopenxr.so',
  'assets/Noto-OFL.txt', 'assets/Liberation-OFL.txt', 'assets/HTC-OpenXR-LICENSE.txt'].join('\n');
test('APK validation rejects wrong versions, debug flags, other ABIs, missing XR declarations and libraries', () => {
  const info = { version: '0.2.0', versionCode: 2 };
  assert.doesNotThrow(() => checkApkReports(badging, manifest, entries, info));
  for (const invalid of [badging.replace("'2'", "'1'"), badging + 'application-debuggable',
    badging.replace("'arm64-v8a'", "'arm64-v8a' 'armeabi-v7a'")])
    assert.throws(() => checkApkReports(invalid, manifest, entries, info));
  assert.throws(() => checkApkReports(badging, '', entries, info), /Manifest/);
  assert.throws(() => checkApkReports(badging, manifest, entries.replace('libUnityOpenXR.so', 'missing.so'), info), /必須ファイル/);
});
