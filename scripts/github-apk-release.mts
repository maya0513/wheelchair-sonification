import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { apkName, buildRelease } from './unity-release.mts';
import { repository } from './unity-xr-sdk.mts';
import { signingEnvironment, signingVariables } from './android-signing.mts';
import { runPreview, validatePreview } from './unity-preview.mts';
import { completedSoak } from './console-validation.mts';
import { captureConsole } from './console-preview.mts';
import { validateConsolePreview, packageConsole, packagePreviewPair } from './console-packaging.mts';

export const releaseRepository = 'maya0513/wheelchair-sonification';
type Result = { status: number | null; stdout: string; stderr: string };
function execute(command: string, args: string[], cwd: string, env: NodeJS.ProcessEnv): Result {
  const result = spawnSync(command, args, { cwd, env, encoding: 'utf8', shell: false, maxBuffer: 8 * 1024 * 1024 });
  return { status: result.status, stdout: result.stdout || '', stderr: result.stderr || '' };
}

type PublishOptions = { root: string; env: NodeJS.ProcessEnv; run: typeof execute; build: typeof buildRelease; preview: typeof runPreview;
  soakValidation?: typeof completedSoak; consolePreview?: typeof captureConsole; consolePackage?: typeof packageConsole; previewPackage?: typeof packagePreviewPair };
export async function publishRelease(tag: string, code: string, options: PublishOptions = {
  root: repository, env: process.env, run: execute, build: buildRelease, preview: runPreview,
}) {
  if (!/^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*)?$/.test(tag))
    throw new Error('タグはv0.2.0のような版番号にしてください。');
  if (!/^[1-9]\d*$/.test(code) || Number(code) > 2100000000) throw new Error('versionCodeは1〜2100000000の整数にしてください。');
  const { root, run, build, preview } = options;
  const signing = signingEnvironment(root, options.env);
  const env = options.env; // GitHub/Git processes do not need the locally loaded signing passwords.
  if (signingVariables.some(name => !signing[name]) || !existsSync(resolve(root, signing.UNITY_ANDROID_KEYSTORE || '.missing-signing-key')))
    throw new Error('配布用の署名設定がありません。just android-signing-initまたは署名環境変数を設定してください。');
  const checked = (command: string, args: string[]) => {
    const result = run(command, args, root, env);
    if (result.status !== 0) throw new Error(`公開前チェックまたはGitHub操作に失敗しました: ${command} ${args.slice(0, 2).join(' ')}。認証・権限・接続を確認してください。`);
    return result.stdout.trim();
  };
  checked('gh', ['auth', 'status', '--active']);
  const remote = checked('git', ['remote', 'get-url', 'origin']);
  if (!['https://github.com/' + releaseRepository + '.git', 'https://github.com/' + releaseRepository,
    'git@github.com:' + releaseRepository + '.git'].includes(remote)) throw new Error('originが所定の公開先ではありません。');
  if (checked('git', ['status', '--porcelain'])) throw new Error('未コミットの変更があります。確認してcommit・pushしてから公開してください。');
  const commit = checked('git', ['rev-parse', 'HEAD']);
  const remoteCommit = checked('gh', ['api', `repos/${releaseRepository}/commits/${commit}`, '--jq', '.sha']);
  if (remoteCommit !== commit) throw new Error('対象コミットをGitHubで確認できません。先にpushしてください。');
  for (const endpoint of [`repos/${releaseRepository}/git/ref/tags/${tag}`, `repos/${releaseRepository}/releases/tags/${tag}`]) {
    const result = run('gh', ['api', endpoint], root, env);
    if (result.status === 0) throw new Error('指定タグまたはReleaseは既に存在します。上書きせず、新しい版を指定してください。');
    if (!result.stderr.includes('HTTP 404')) throw new Error('GitHubのタグ・Releaseの有無を確認できません。公開を停止しました。');
  }
  const soak = (options.soakValidation || completedSoak)(root);
  const previewDirectory = await preview(root, 'capture', undefined, env);
  const previewManifest = validatePreview(previewDirectory, commit);
  if (previewManifest.source_dirty !== false) throw new Error('未コミット変更を含む画像は公開できません。');
  const operatorDirectory = await (options.consolePreview || captureConsole)(join(root, '.local/console-preview'));
  const operatorManifest = validateConsolePreview(operatorDirectory, commit);
  if (operatorManifest.source_dirty !== false) throw new Error('未コミット変更を含む実施者画面は公開できません。');
  const output = await build(root, { ...signing, UNITY_BUILD_VERSION: tag.slice(1), UNITY_ANDROID_VERSION_CODE: code });
  const info = JSON.parse(readFileSync(join(output, 'build-info.json'), 'utf8'));
  if (info.gitCommit !== commit || info.gitDirty !== false || info.version !== tag.slice(1) || info.versionCode !== Number(code)
    || info.targetDevice !== 'VIVE Focus Vision' || info.xrLoader !== 'OpenXR' || info.inputMode !== 'simulated-and-vive-ultimate-tracker'
    || info.operatorProtocol !== 1 || info.headsetUi !== 'participant-only' || info.androidTargetSdk !== 36
    || info.validation?.signatureVerified !== true || info.validation?.xrManifestVerified !== true)
    throw new Error('公開するビルドの版・コミット・検証情報を確認できません。');
  if (checked('git', ['rev-parse', 'HEAD']) !== commit || checked('git', ['status', '--porcelain']))
    throw new Error('ビルド中にソースが変更されました。公開を停止しました。');
  const previewName = 'ui-preview.zip';
  (options.previewPackage || packagePreviewPair)(previewDirectory, operatorDirectory, join(output, previewName));
  const consoleName = 'operator-console.zip';
  (options.consolePackage || packageConsole)(root, join(output, consoleName), commit, info.version);
  info.uiPreview = { file: previewName, headsetShots: previewManifest.shots.length, operatorShots: operatorManifest.shots.length, sourceCommit: commit, physicalDeviceMeasured: false };
  info.soakValidation = soak;
  info.operatorConsole = { file: consoleName, sourceCommit: commit, protocol: 1, localOnly: true };
  writeFileSync(join(output, 'build-info.json'), JSON.stringify(info, null, 2) + '\n');
  const hashedFiles = [apkName, 'build-info.json', previewName, consoleName];
  writeFileSync(join(output, 'SHA256SUMS.txt'), hashedFiles.map(file =>
    createHash('sha256').update(readFileSync(join(output, file))).digest('hex') + '  ' + file).join('\n') + '\n');
  const files = [...hashedFiles, 'SHA256SUMS.txt'];
  for (const file of files) if (!existsSync(join(output, file)) || statSync(join(output, file)).size === 0)
    throw new Error('公開するファイルがありません: ' + file);
  const notes = join(output, 'release-notes.md');
  writeFileSync(notes, `VIVE Focus Vision向けのAndroid / OpenXR単体実行用APKです。\n\n` +
    `被験者はアプリやコントローラーを操作しません。実施者のPCブラウザーから、5台の役割設定・3次元校正、来訪の課題順・理解確認・訓練・20分休憩、元データ保存・ZIP回収を操作します。実測入力と明示した合成入力を備えます。\n\n` +
    `**この版の実機インストール、5台同時追跡、推定角の精度、出力遅延、屋外条件、90分稼働は未確認です。本計測の受入れ条件は未達です。** 最初は停止・着座して確認してください。接触候補は位置姿勢による推定で、接触力は測定しません。\n\n` +
    `operator-console.zipを展開し、Node.js 24を準備した実施者用Ubuntu PCで sh start.sh を実行してください。PCのホットスポットにゴーグルを接続し、表示された確認番号をPCへ入力します。現地の外部回線は不要です。\n\nui-preview.zipを展開してindex.htmlを開くと、被験者用のUnity描画と実施者用のブラウザー画像を確認できます。実機の光学系・パススルーを再現した画像ではありません。記録データはReleaseに含みません。\n\n` +
    `- Source: ${commit}\n- Version: ${info.version} / Android versionCode ${info.versionCode}\n` +
    `- Unity: ${info.unityVersion}\n- VIVE OpenXR: ${info.viveOpenXrVersion} / Unity OpenXR: ${info.unityOpenXrVersion}\n` +
    `- ARM64 / IL2CPP / 非Development Build / 署名検証済み\n- SHA-256: ${info.sha256}\n\n` +
    `PC上のUnity＋操作サーバーでは、合成入力による来訪完了・ZIP回収と${Math.floor(soak.realElapsedSeconds)}秒の継続稼働を確認しました。Focus Vision本体の90分検証とは別です。\n\n` +
    `APKをダウンロードし、本体のDownloadへUSBでコピーしてファイルブラウザからインストールしてください。\n` +
    `[導入・操作手順](https://github.com/${releaseRepository}/blob/${tag}/docs/development/android-build.md)\n`);
  let draftCreated = false;
  try {
    checked('gh', ['release', 'create', tag, '--repo', releaseRepository, '--target', commit, '--draft', '--prerelease',
      '--title', `Focus Vision ${tag}`, '--notes-file', notes]);
    draftCreated = true;
    checked('gh', ['release', 'upload', tag, '--repo', releaseRepository, ...files.map(file => join(output, file))]);
    const assets = JSON.parse(checked('gh', ['release', 'view', tag, '--repo', releaseRepository, '--json', 'assets'])).assets;
    for (const file of files) if (!assets.some((asset: { name: string; size: number }) =>
      asset.name === file && asset.size === statSync(join(output, file)).size)) throw new Error('添付ファイルの名前・サイズを確認できません。');
    checked('gh', ['release', 'edit', tag, '--repo', releaseRepository, '--draft=false', '--prerelease', '--latest=false']);
    const url = checked('gh', ['release', 'view', tag, '--repo', releaseRepository, '--json', 'url', '--jq', '.url']);
    console.log('プレリリースを公開しました: ' + url);
    return url;
  } catch (error) {
    if (draftCreated) throw new Error(`公開処理を完了できませんでした。${tag}のDraftをGitHub Releasesで確認してください。` +
      `\nローカルの成果物: ${output}\n添付完了を確認するまで公開しないでください。\n` + String(error));
    throw error;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: just release-apk v0.2.0 2');
    await publishRelease(process.argv[2], process.argv[3]);
  } catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
}
