import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { apkName, buildRelease } from './unity-release.mts';
import { repository } from './unity-xr-sdk.mts';
import { signingEnvironment, signingVariables } from './android-signing.mts';
export const releaseRepository = 'maya0513/wheelchair-sonification';
type Result = { status: number | null; stdout: string; stderr: string };
function execute(command: string, args: string[], cwd: string, env: NodeJS.ProcessEnv): Result {
  const result = spawnSync(command, args, {
    cwd,
    env,
    encoding: 'utf8',
    shell: false,
    maxBuffer: 8 * 1024 * 1024,
  });
  return { status: result.status, stdout: result.stdout || '', stderr: result.stderr || '' };
}

type PublishOptions = {
  root: string;
  env: NodeJS.ProcessEnv;
  run: typeof execute;
  build: typeof buildRelease;
};
export async function publishRelease(
  tag: string,
  code: string,
  options: PublishOptions = {
    root: repository,
    env: process.env,
    run: execute,
    build: buildRelease,
  },
) {
  if (
    !/^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*)?$/.test(tag)
  )
    throw new Error('タグはv0.2.0のような版番号にしてください。');
  if (!/^[1-9]\d*$/.test(code) || Number(code) > 2100000000)
    throw new Error('versionCodeは1〜2100000000の整数にしてください。');
  const { root, run, build } = options;
  const signing = signingEnvironment(root, options.env);
  const env = options.env; // GitHub/Git processes do not need the locally loaded signing passwords.
  if (
    signingVariables.some((name) => !signing[name]) ||
    !existsSync(resolve(root, signing.UNITY_ANDROID_KEYSTORE || '.missing-signing-key'))
  )
    throw new Error(
      '配布用の署名設定がありません。just dev android-signing-initまたは署名環境変数を設定してください。',
    );
  const checked = (command: string, args: string[]) => {
    const result = run(command, args, root, env);
    if (result.status !== 0)
      throw new Error(
        `公開前チェックまたはGitHub操作に失敗しました: ${command} ${args.slice(0, 2).join(' ')}。認証・権限・接続を確認してください。`,
      );
    return result.stdout.trim();
  };
  checked('gh', ['auth', 'status', '--active']);
  const remote = checked('git', ['remote', 'get-url', 'origin']);
  if (
    ![
      'https://github.com/' + releaseRepository + '.git',
      'https://github.com/' + releaseRepository,
      'git@github.com:' + releaseRepository + '.git',
    ].includes(remote)
  )
    throw new Error('originが所定の公開先ではありません。');
  if (checked('git', ['status', '--porcelain']))
    throw new Error('未コミットの変更があります。確認してcommit・pushしてから公開してください。');
  const commit = checked('git', ['rev-parse', 'HEAD']);
  const remoteCommit = checked('gh', [
    'api',
    `repos/${releaseRepository}/commits/${commit}`,
    '--jq',
    '.sha',
  ]);
  if (remoteCommit !== commit)
    throw new Error('対象コミットをGitHubで確認できません。先にpushしてください。');
  for (const endpoint of [
    `repos/${releaseRepository}/git/ref/tags/${tag}`,
    `repos/${releaseRepository}/releases/tags/${tag}`,
  ]) {
    const result = run('gh', ['api', endpoint], root, env);
    if (result.status === 0)
      throw new Error(
        '指定タグまたはReleaseは既に存在します。上書きせず、新しい版を指定してください。',
      );
    if (!result.stderr.includes('HTTP 404'))
      throw new Error('GitHubのタグ・Releaseの有無を確認できません。公開を停止しました。');
  }
  const output = await build(root, {
    ...signing,
    UNITY_BUILD_VERSION: tag.slice(1),
    UNITY_ANDROID_VERSION_CODE: code,
  });
  const info = JSON.parse(readFileSync(join(output, 'build-info.json'), 'utf8'));
  if (
    info.gitCommit !== commit ||
    info.gitDirty !== false ||
    info.version !== tag.slice(1) ||
    info.versionCode !== Number(code) ||
    info.targetDevice !== 'VIVE Focus Vision' ||
    info.xrLoader !== 'OpenXR' ||
    info.inputMode !== 'simulated-and-vive-ultimate-tracker' ||
    info.operatorProtocol !== 3 ||
    info.headsetUi !== 'guided-preparation-and-participant' ||
    info.androidTargetSdk !== 36 ||
    info.validation?.signatureVerified !== true ||
    info.validation?.xrManifestVerified !== true
  )
    throw new Error('公開するビルドの版・コミット・検証情報を確認できません。');
  if (checked('git', ['rev-parse', 'HEAD']) !== commit || checked('git', ['status', '--porcelain']))
    throw new Error('ビルド中にソースが変更されました。公開を停止しました。');
  const apk = join(output, apkName);
  if (!existsSync(apk) || statSync(apk).size === 0) throw new Error('公開するAPKがありません。');
  const sha256 = createHash('sha256').update(readFileSync(apk)).digest('hex');
  if (info.sha256 !== sha256) throw new Error('APKとビルド情報のSHA-256が一致しません。');
  const files = [apkName];
  const notes = join(output, 'release-notes.md');
  writeFileSync(
    notes,
    `VIVE Focus Vision向けのAndroid / OpenXR単体実行用APKです。\n\n` +
      `APKを本体のDownloadへUSBでコピーし、ファイルブラウザーからインストールしてください。\n` +
      `PC側は同じ版のリポジトリで \`just start\` を実行します。\n\n` +
      `[画像付きの説明](https://github.com/${releaseRepository}/blob/${tag}/docs/app-overview.md) · ` +
      `[導入・操作手順](https://github.com/${releaseRepository}/blob/${tag}/docs/operations/setup.md)\n\n` +
      `このAPKでの実機インストール、追跡、音・表示・視線、走行中の動作は未確認です。` +
      `ソフトウェア試験の成功を実機性能の検証とは扱いません。\n\n` +
      `- Source: ${commit}\n- Version: ${info.version} / Android versionCode ${info.versionCode}\n` +
      `- Unity: ${info.unityVersion}\n- VIVE OpenXR: ${info.viveOpenXrVersion} / Unity OpenXR: ${info.unityOpenXrVersion}\n` +
      `- ARM64 / IL2CPP / 非Development Build / 署名検証済み\n- SHA-256: ${sha256}\n`,
  );
  let draftCreated = false;
  try {
    checked('gh', [
      'release',
      'create',
      tag,
      '--repo',
      releaseRepository,
      '--target',
      commit,
      '--draft',
      '--prerelease',
      '--title',
      `Focus Vision ${tag}`,
      '--notes-file',
      notes,
    ]);
    draftCreated = true;
    checked('gh', [
      'release',
      'upload',
      tag,
      '--repo',
      releaseRepository,
      ...files.map((file) => join(output, file)),
    ]);
    const assets = JSON.parse(
      checked('gh', ['release', 'view', tag, '--repo', releaseRepository, '--json', 'assets']),
    ).assets;
    if (assets.length !== 1) throw new Error('添付物がAPK一件だけであることを確認できません。');
    for (const file of files)
      if (
        !assets.some(
          (asset: { name: string; size: number }) =>
            asset.name === file && asset.size === statSync(join(output, file)).size,
        )
      )
        throw new Error('添付ファイルの名前・サイズを確認できません。');
    checked('gh', [
      'release',
      'edit',
      tag,
      '--repo',
      releaseRepository,
      '--draft=false',
      '--prerelease',
      '--latest=false',
    ]);
    const url = checked('gh', [
      'release',
      'view',
      tag,
      '--repo',
      releaseRepository,
      '--json',
      'url',
      '--jq',
      '.url',
    ]);
    console.log('プレリリースを公開しました: ' + url);
    return url;
  } catch (error) {
    if (draftCreated)
      throw new Error(
        `公開処理を完了できませんでした。${tag}のDraftをGitHub Releasesで確認してください。` +
          `\nローカルの成果物: ${output}\n添付完了を確認するまで公開しないでください。\n` +
          String(error),
      );
    throw error;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: just dev release-apk v0.7.0 7');
    await publishRelease(process.argv[2], process.argv[3]);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
