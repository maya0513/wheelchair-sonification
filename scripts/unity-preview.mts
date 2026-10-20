import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { editorPath } from './unity-editor.mts';
import { repository, prepareXrSdk } from './unity-xr-sdk.mts';
import { snapshotUnitySettings } from './unity-state.mts';

type Shot = { name: string; file: string; title: string; crop_title?: string; crop_button?: string; crop_gauge?: string;
  assumed_vertical_fov_degrees: number; light_background: boolean; intentional_world_panel_offscreen: boolean };
export type PreviewManifest = { passed: boolean; physical_device_measured: boolean; source_commit: string; unity_version: string;
  shots: Shot[]; errors: string[]; source_dirty?: boolean; source_fingerprint?: string };
const escape = (s: unknown) => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

export function gallery(manifest: PreviewManifest) {
  return '<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>XR画面確認</title>' +
    '<style>body{font:18px/1.7 sans-serif;background:#eee;color:#111;margin:24px}main{max-width:1500px;margin:auto}h1{font-size:32px}code{overflow-wrap:anywhere}' +
    '.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:24px}article{background:white;padding:20px;border:1px solid #777}' +
    'img{width:100%;height:auto;display:block}a{color:#034b9a}.crops{display:flex;gap:12px;flex-wrap:wrap}.crops figure{margin:0;max-width:100%}.crops img{width:auto;max-width:100%;image-rendering:auto}' +
    'figcaption,small{font-size:16px}button{font-size:18px;padding:10px}body.hide-crops .crops{display:none}</style><main>' +
    '<h1>Focus Vision：Unity描画の画像一覧</h1><p>実際のアプリと共通のUI・フォントをUnity Editorで描画した画像です。視野角と背景は仮定です。' +
    'レンズ越しの可読性、パススルー画質、実機速度・遅延を再現したものではありません。サムネイルを押すと元画像を開きます。</p>' +
    `<p>Unity ${escape(manifest.unity_version)} ／ Source <code>${escape(manifest.source_commit)}</code> ／ 未コミット変更 ${manifest.source_dirty ? 'あり' : 'なし'}</p>` +
    '<p><a href="contact-sheet.png">一覧PNG</a> ／ <a href="manifest.json">撮影条件・検査結果</a>　<button onclick="document.body.classList.toggle(\'hide-crops\')">切り出し画像を切替</button></p><div class="grid">' +
    manifest.shots.map(shot => `<article><h2>${escape(shot.title)}</h2><p>仮の垂直視野角 ${shot.assumed_vertical_fov_degrees}° ／ ${shot.light_background ? '明' : '暗'}背景` +
      (shot.intentional_world_panel_offscreen ? ' ／ 頭部回転で画面が視野を外れる状態を意図的に撮影' : '') + '</p>' +
      `<a href="${escape(shot.file)}" target="_blank"><img loading="lazy" src="${escape(shot.file)}" alt="${escape(shot.title)}"></a><div class="crops">` +
      (['title', 'button', 'gauge'] as const).map(kind => {
        const file = shot[`crop_${kind}`]; return file ? `<figure><figcaption>${({ title: '文字', button: '操作ボタン', gauge: 'ゲージ' })[kind]}・元の画素で切り出し</figcaption><a href="${escape(file)}" target="_blank"><img loading="lazy" src="${escape(file)}" alt="${kind}"></a></figure>` : '';
      }).join('') + '</div></article>').join('') + '</div></main></html>\n';
}

export function validatePreview(directory: string, commit?: string) {
  const manifest = JSON.parse(readFileSync(join(directory, 'manifest.json'), 'utf8')) as PreviewManifest;
  if (!manifest.passed || manifest.physical_device_measured !== false || !Array.isArray(manifest.shots) || manifest.shots.length < 18 || manifest.errors.length)
    throw new Error('今回のUnity画面確認が成功していません。manifest.jsonと画像を確認してください。');
  if (commit && manifest.source_commit !== commit) throw new Error('画像とソースコミットが一致しません。');
  const png = (file: string) => {
    if (!/^[a-zA-Z0-9_-]+\.png$/.test(file) || !existsSync(join(directory, file)) || statSync(join(directory, file)).size < 100)
      throw new Error('確認画像がありません、またはファイル名が不正です。');
    const bytes = readFileSync(join(directory, file));
    if (bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a' || bytes.toString('ascii', 12, 16) !== 'IHDR'
      || bytes.readUInt32BE(16) === 0 || bytes.readUInt32BE(20) === 0) throw new Error('確認画像のPNG形式が不正です。');
  };
  for (const shot of manifest.shots) for (const file of [shot.file, shot.crop_title, shot.crop_button, shot.crop_gauge].filter(Boolean) as string[]) png(file);
  png('contact-sheet.png');
  return manifest;
}

export function createPreviewArchive(directory: string, output: string) {
  validatePreview(directory);
  const script = 'import pathlib,sys,zipfile\np=pathlib.Path(sys.argv[1])\nwith zipfile.ZipFile(sys.argv[2],"x",zipfile.ZIP_DEFLATED) as z:\n for f in sorted(p.iterdir()):\n  if f.is_file() and (f.suffix==".png" or f.name in ("index.html","manifest.json")):\n   z.write(f,f.name)\n';
  const r = spawnSync('python3', ['-c', script, directory, output], { encoding: 'utf8', shell: false });
  if (r.status !== 0) throw new Error('画面確認ZIPを作れませんでした: ' + r.stderr);
}

function fingerprint(root: string) {
  const files: string[] = [];
  const walk = (directory: string) => { for (const f of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, f.name); if (f.isDirectory()) walk(path); else if (f.name.endsWith('.cs')) files.push(path);
  } };
  walk(join(root, 'wheelchair-sonification/Assets/Experiment'));
  files.push(join(root, 'wheelchair-sonification/Assets/Settings/Mobile_RPAsset.asset'));
  const hash = createHash('sha256');
  for (const path of files.sort()) hash.update(path.slice(root.length)).update(readFileSync(path));
  return hash.digest('hex');
}

export async function runPreview(root = repository, mode = 'capture', source?: string, env = process.env) {
  const project = join(root, 'wheelchair-sonification');
  const version = /^m_EditorVersion: (\S+)$/m.exec(readFileSync(join(project, 'ProjectSettings/ProjectVersion.txt'), 'utf8'))?.[1];
  if (!version) throw new Error('Unityの版を確認できません。');
  if (existsSync(join(project, 'Temp/UnityLockfile'))) throw new Error('Unity Editorを閉じてから実行してください。');
  await prepareXrSdk(root);
  const editor = editorPath(version, process.platform, env);
  mkdirSync(join(root, '.local/logs'), { recursive: true });
  const commit = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim();
  const dirty = spawnSync('git', ['status', '--porcelain'], { cwd: root, encoding: 'utf8' }).stdout.trim().length > 0;
  const log = join(root, '.local/logs/unity-' + (mode === 'capture' ? 'preview' : mode === 'open' ? 'preview-open' : 'replay') + '.log');
  if (existsSync(log)) renameSync(log, log + '.previous');
  const staging = mode === 'capture' ? mkdtempSync(join(root, '.local/.ui-preview-')) : '';
  const replayOutput = join(root, '.local/replay-reports', source ? basename(resolve(source)) + '-' + Date.now() : 'unknown');
  const method = mode === 'open' ? 'OpenInteractive' : mode === 'capture' ? 'Capture' : 'Replay';
  const restore = snapshotUnitySettings(project);
  try {
    const args = ['-projectPath', project, '-logFile', log, '-executeMethod', 'WheelchairSonification.Editor.XrPreviewTools.' + method];
    if (mode !== 'open' || env.WHEELCHAIR_INTERACTIVE_SMOKE === '1') args.unshift('-batchmode');
    if (mode === 'replay') args.unshift('-nographics', '-quit');
    else args.unshift(env.UNITY_GRAPHICS_API === 'opengl' ? '-force-glcore' : '-force-vulkan');
    console.log('Unityの' + (mode === 'capture' ? '実描画を撮影' : mode === 'open' ? '模擬操作を起動' : '記録を再解析') + 'します。ログ: ' + log);
    const code = await new Promise<number>((accept, reject) => {
      const child = spawn(editor, args, { cwd: root, shell: false, stdio: ['ignore', 'inherit', 'inherit'], env: {
        ...env, WHEELCHAIR_UI_PREVIEW_OUTPUT: staging, WHEELCHAIR_SOURCE_COMMIT: commit,
        WHEELCHAIR_REPLAY_SOURCE: source ? resolve(source) : '', WHEELCHAIR_REPLAY_OUTPUT: replayOutput,
      } });
      const bounded = mode !== 'open' || env.WHEELCHAIR_INTERACTIVE_SMOKE === '1';
      const timer = bounded ? setTimeout(() => child.kill('SIGTERM'), 10 * 60 * 1000) : undefined;
      const watchdog = bounded ? setInterval(() => {
        if (existsSync(log) && /(?:NullReference|MissingReference|InvalidOperation)Exception:|Can't add script behaviour/.test(readFileSync(log, 'utf8')))
          child.kill('SIGTERM');
      }, 2000) : undefined;
      const cleanup = () => { if (timer) clearTimeout(timer); if (watchdog) clearInterval(watchdog); };
      child.once('error', e => { cleanup(); reject(e); }); child.once('exit', c => { cleanup(); accept(c ?? 1); });
    });
    if (code !== 0 || !existsSync(log) || /error CS\d+|Scripts have compiler errors|(?:NullReference|MissingReference|InvalidOperation)Exception:|Can't add script behaviour/.test(readFileSync(log, 'utf8')))
      throw new Error('Unity確認に失敗しました。ログ: ' + log + (staging ? '\n未採用の確認画像: ' + staging : ''));
    if (mode === 'capture') {
      const manifest = validatePreview(staging, commit);
      manifest.source_dirty = dirty; manifest.source_fingerprint = fingerprint(root);
      writeFileSync(join(staging, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
      writeFileSync(join(staging, 'index.html'), gallery(manifest));
      const output = join(root, '.local/ui-preview');
      rmSync(output + '.previous', { recursive: true, force: true });
      if (existsSync(output)) renameSync(output, output + '.previous');
      renameSync(staging, output);
      console.log('画面一覧: ' + join(output, 'index.html'));
      return output;
    }
    return replayOutput;
  } finally { restore(); }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const mode = process.argv[2] ?? 'capture';
  try {
    if (!['capture', 'open', 'replay'].includes(mode) || mode === 'replay' && !process.argv[3]) throw new Error('Usage: unity-preview.mts {capture|open|replay session-directory}');
    await runPreview(repository, mode, process.argv[3]);
  } catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
}
