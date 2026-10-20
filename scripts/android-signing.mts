import { randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { repository } from './unity-xr-sdk.mts';
import { editorPath } from './unity-editor.mts';

export const signingVariables = ['UNITY_ANDROID_KEYSTORE', 'UNITY_ANDROID_KEY_ALIAS',
  'UNITY_ANDROID_KEYSTORE_PASSWORD', 'UNITY_ANDROID_KEY_ALIAS_PASSWORD'] as const;

// Explicit environment settings must be complete; never mix two signing identities.
export function signingEnvironment(root: string, env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  if (signingVariables.some(name => env[name])) return { ...env };
  const profile = join(root, '.local/android-signing.json');
  if (!existsSync(profile)) return { ...env };
  let values: Record<string, string>;
  try { values = JSON.parse(readFileSync(profile, 'utf8')); }
  catch { throw new Error('ローカルの署名設定を読めません: ' + profile); }
  if (signingVariables.some(name => typeof values[name] !== 'string' || !values[name]))
    throw new Error('ローカルの署名設定に必要な項目がありません: ' + profile);
  return { ...env, ...Object.fromEntries(signingVariables.map(name => [name, values[name]])) };
}

export function initializeSigning(root: string, editor: string, env: NodeJS.ProcessEnv, run = spawnSync) {
  const directory = join(root, '.local/signing');
  const profile = join(root, '.local/android-signing.json');
  const keystore = join(directory, 'wheelchair-sonification.keystore');
  if (existsSync(profile) || existsSync(keystore)) throw new Error('署名鍵または設定が既にあります。更新用の鍵を上書きしません。');
  const data = process.platform === 'darwin' ? join(editor, '../../PlaybackEngines/AndroidPlayer')
    : join(dirname(editor), 'Data/PlaybackEngines/AndroidPlayer');
  const keytool = join(data, 'OpenJDK/bin', process.platform === 'win32' ? 'keytool.exe' : 'keytool');
  if (!existsSync(keytool)) throw new Error('UnityのOpenJDKがありません。Android Build Supportを導入してください。');
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  if (process.platform !== 'win32') chmodSync(directory, 0o700);
  const password = randomBytes(36).toString('base64url');
  let created = false;
  try {
    const result = run(keytool, ['-genkeypair', '-keystore', keystore, '-storetype', 'PKCS12',
      '-alias', 'wheelchair-sonification', '-keyalg', 'RSA', '-keysize', '3072', '-sigalg', 'SHA256withRSA',
      '-validity', '10000', '-dname', 'CN=Wheelchair Sonification',
      '-storepass:env', 'WHEELCHAIR_NEW_KEY_PASSWORD', '-keypass:env', 'WHEELCHAIR_NEW_KEY_PASSWORD'],
    { env: { ...env, WHEELCHAIR_NEW_KEY_PASSWORD: password }, encoding: 'utf8', shell: false });
    if (result.status !== 0 || !existsSync(keystore)) throw new Error('署名鍵の作成に失敗しました。OpenJDKと保存先を確認してください。');
    created = true;
    if (process.platform !== 'win32') chmodSync(keystore, 0o600);
    const values = { UNITY_ANDROID_KEYSTORE: keystore, UNITY_ANDROID_KEY_ALIAS: 'wheelchair-sonification',
      UNITY_ANDROID_KEYSTORE_PASSWORD: password, UNITY_ANDROID_KEY_ALIAS_PASSWORD: password };
    writeFileSync(profile, JSON.stringify(values, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
    console.log(`配布用の署名鍵を作成しました。\n鍵: ${keystore}\n設定: ${profile}\n両ファイルを安全な場所へバックアップしてください。パスワードは表示しません。`);
  } catch (error) {
    if (!created) rmSync(keystore, { force: true });
    throw error;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const version = /^m_EditorVersion: (\S+)$/m.exec(readFileSync(join(repository,
      'wheelchair-sonification/ProjectSettings/ProjectVersion.txt'), 'utf8'))?.[1];
    if (!version) throw new Error('Unityの版を読めません。');
    initializeSigning(repository, editorPath(version, process.platform, process.env), process.env);
  } catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
}
