import { createHash } from 'node:crypto';
import { createReadStream, createWriteStream, existsSync, mkdirSync, openSync, closeSync, renameSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';

export const viveSdk = Object.freeze({
  version: '2.5.1',
  name: 'com.htc.upm.vive.openxr-2.5.1.tgz',
  url: 'https://github.com/ViveSoftware/VIVE-OpenXR-Unity/releases/download/versions/2.5.1/com.htc.upm.vive.openxr-2.5.1.tgz',
  sha256: '250c55f8cfd65c28c29f52ccb8b8864ebaa64f47665c74c50186ed5d388ccf80',
});
export const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');

export async function fileSha256(path: string) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

// The official release archive contains native binaries; Git source archives contain LFS pointers.
export async function prepareXrSdk(root = repository, fetcher = fetch, spec = viveSdk) {
  const directory = join(root, '.local/unity-packages');
  const destination = join(directory, spec.name);
  mkdirSync(directory, { recursive: true });
  if (existsSync(destination) && await fileSha256(destination) === spec.sha256) return destination;
  const lock = destination + '.lock';
  let handle: number;
  try { handle = openSync(lock, 'wx'); }
  catch { throw new Error(`HTC SDKを別の処理が準備中です。停止済みならロックを確認してください: ${lock}`); }
  closeSync(handle);
  const temporary = destination + '.download';
  try {
    if (existsSync(destination) && await fileSha256(destination) === spec.sha256) return destination;
    console.log(`HTC OpenXR ${spec.version}を公式配布元から取得します（初回約378 MB）。`);
    const response = await fetcher(spec.url, { signal: AbortSignal.timeout(10 * 60 * 1000) });
    if (!response.ok || !response.body) throw new Error(`HTC SDKの取得に失敗しました: HTTP ${response.status}`);
    await pipeline(Readable.fromWeb(response.body), createWriteStream(temporary, { flags: 'w' }));
    if (await fileSha256(temporary) !== spec.sha256) throw new Error('HTC SDKのSHA-256が固定値と一致しません。取得したファイルは使用しません。');
    // Windows cannot rename over an existing destination.
    rmSync(destination, { force: true });
    renameSync(temporary, destination);
    return destination;
  } finally {
    rmSync(temporary, { force: true });
    rmSync(lock, { force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await prepareXrSdk(); }
  catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
}
