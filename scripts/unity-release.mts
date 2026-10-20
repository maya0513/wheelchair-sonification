import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { closeSync, createReadStream, existsSync, mkdirSync, mkdtempSync, openSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { prepareXrSdk } from './unity-xr-sdk.mts';
import { signingEnvironment, signingVariables } from './android-signing.mts';
import { verifyApk } from './android-apk-validation.mts';
import { editorPath } from './unity-editor.mts';
import { snapshotUnitySettings } from './unity-state.mts';
export { editorPath } from './unity-editor.mts';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const apkName = 'WheelchairSonification.apk';

type Invocation = { editor: string; args: string[]; env: NodeJS.ProcessEnv; cwd: string };
async function launchUnity({ editor, args, env, cwd }: Invocation): Promise<number> {
  return await new Promise((accept, reject) => {
    const child = spawn(editor, args, { cwd, env, stdio: ['ignore', 'inherit', 'inherit'], shell: false });
    child.once('error', reject);
    child.once('exit', code => accept(code ?? 1));
  });
}

export async function buildRelease(root = repository, env = process.env, runUnity = launchUnity,
  dependencies = { prepare: prepareXrSdk, verify: verifyApk }) {
  env = signingEnvironment(root, env);
  const project = join(root, 'wheelchair-sonification');
  const version = /^m_EditorVersion: (\S+)\r?$/m.exec(readFileSync(join(project, 'ProjectSettings/ProjectVersion.txt'), 'utf8'))?.[1];
  if (!version) throw new Error('ProjectVersion.txtからUnityの版を読めません。');
  const editor = editorPath(version, process.platform, env);
  if (!existsSync(editor) || !statSync(editor).isFile()) throw new Error(`Unity ${version} がありません。Hubで導入するか、UNITY_EDITORを指定してください: ${editor}`);
  if (existsSync(join(project, 'Temp/UnityLockfile'))) throw new Error('このプロジェクトのUnity Editorを閉じてからビルドしてください。');
  const missing = signingVariables.filter(name => !env[name]);
  if (missing.length) throw new Error(`署名用の環境変数がありません: ${missing.join(', ')}。docs/development/android-build.mdを参照してください。`);
  const keystore = resolve(root, env.UNITY_ANDROID_KEYSTORE!);
  if (!existsSync(keystore) || !statSync(keystore).isFile()) throw new Error(`署名用キーストアがありません: ${keystore}`);
  if (env.UNITY_ANDROID_VERSION_CODE && (!/^[1-9]\d*$/.test(env.UNITY_ANDROID_VERSION_CODE) || Number(env.UNITY_ANDROID_VERSION_CODE) > 2100000000)) {
    throw new Error('UNITY_ANDROID_VERSION_CODEは1〜2100000000の整数で指定してください。');
  }
  await dependencies.prepare(root);

  const builds = join(root, '.local/builds');
  const log = join(root, '.local/logs/unity-release-android.log');
  const output = join(builds, 'android-release');
  const lock = join(builds, '.android-release.lock');
  mkdirSync(builds, { recursive: true });
  mkdirSync(dirname(log), { recursive: true });
  let lockHandle: number;
  try { lockHandle = openSync(lock, 'wx'); }
  catch { throw new Error(`別のAPKビルドが実行中です。停止済みならロックを確認してください: ${lock}`); }
  closeSync(lockHandle);
  let staging: string | undefined;
  const restoreSettings = snapshotUnitySettings(project);
  const sourceCommit = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', shell: false }).stdout?.trim() || 'unknown';
  try {
    // Unityはビルド時に設定を保存する。Editor終了後に空の辞書等も含めて復元する。
    staging = mkdtempSync(join(builds, '.android-release-'));
    if (existsSync(log)) {
      rmSync(`${log}.previous`, { force: true });
      renameSync(log, `${log}.previous`);
    }
    console.log(`Unity ${version}: Androidリリースビルドを開始します。ログ: ${log}`);
    const code = await runUnity({ editor, cwd: root,
      args: ['-batchmode', '-nographics', '-quit', '-buildTarget', 'Android', '-projectPath', project, '-logFile', log,
        '-executeMethod', 'WheelchairSonification.Editor.ReleaseBuildTools.BuildAndroid'],
      env: { ...env, UNITY_ANDROID_KEYSTORE: keystore, WHEELCHAIR_BUILD_DIRECTORY: staging, WHEELCHAIR_EXPECTED_UNITY_VERSION: version,
        WHEELCHAIR_SOURCE_COMMIT: sourceCommit },
    });
    restoreSettings();
    if (code !== 0) throw new Error(`Unityが終了コード${code}で失敗しました。ログ: ${log}`);
    if (!existsSync(log)) throw new Error(`今回のUnityログがありません: ${log}`);
    if (/error CS\d+|Scripts have compiler errors|Compilation failed/.test(readFileSync(log, 'utf8'))) throw new Error(`コンパイルエラーがあります。ログ: ${log}`);
    const infoPath = join(staging, 'build-info.json');
    if (!existsSync(infoPath)) throw new Error(`今回のビルド完了記録がありません。ログ: ${log}`);
    const info = JSON.parse(readFileSync(infoPath, 'utf8'));
    const apk = join(staging, apkName);
    if (info.result !== 'Succeeded' || info.target !== 'Android' || info.development !== false || info.unityVersion !== version
      || info.architecture !== 'ARM64' || info.scriptingBackend !== 'IL2CPP' || !info.customSigning
      || info.targetDevice !== 'VIVE Focus Vision' || info.xrLoader !== 'OpenXR' || info.passthrough !== true
      || info.inputMode !== 'simulated-and-vive-ultimate-tracker' || info.scenes?.length !== 1 || info.scenes[0] !== 'Assets/Scenes/FeedbackDemoXR.unity'
      || !existsSync(apk) || statSync(apk).size === 0) throw new Error(`今回の署名付き非開発APKを確認できません。ログ: ${log}`);
    const validation = dependencies.verify(apk, editor, env, info);
    const hash = createHash('sha256');
    for await (const chunk of createReadStream(apk)) hash.update(chunk);
    const sha256 = hash.digest('hex');
    const git = (args: string[]) => spawnSync('git', args, { cwd: root, encoding: 'utf8', shell: false });
    const commit = git(['rev-parse', 'HEAD']);
    const status = git(['status', '--porcelain']);
    Object.assign(info, { apk: apkName, sha256, builtAtUtc: new Date().toISOString(),
      gitCommit: commit.status === 0 ? commit.stdout.trim() : null,
      gitDirty: status.status === 0 ? status.stdout.trim().length > 0 : null, validation });
    writeFileSync(infoPath, JSON.stringify(info, null, 2) + '\n');
    writeFileSync(join(staging, 'SHA256SUMS.txt'), `${sha256}  ${apkName}\n`);
    const previous = `${output}.previous`;
    if (existsSync(output)) {
      rmSync(previous, { recursive: true, force: true });
      renameSync(output, previous);
    }
    try { renameSync(staging, output); }
    catch (error) {
      if (!existsSync(output) && existsSync(previous)) renameSync(previous, output);
      throw error;
    }
    console.log(`APK: ${join(output, apkName)}\nSHA-256: ${sha256}\nビルド情報: ${join(output, 'build-info.json')}`);
    return output;
  } finally {
    try { restoreSettings(); }
    finally {
      try { if (staging) rmSync(staging, { recursive: true, force: true }); }
      finally { rmSync(lock, { force: true }); }
    }
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 2) throw new Error('Usage: node scripts/unity-release.mts');
    await buildRelease();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
