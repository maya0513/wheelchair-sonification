import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';

export function androidTools(editor: string, env = process.env, platform = process.platform) {
  const player = platform === 'darwin' ? join(editor, '../../PlaybackEngines/AndroidPlayer')
    : join(dirname(editor), 'Data/PlaybackEngines/AndroidPlayer');
  const sdk = env.ANDROID_SDK_ROOT || env.ANDROID_HOME || join(player, 'SDK');
  const base = join(sdk, 'build-tools');
  if (!existsSync(base)) throw new Error('Android SDKのbuild-toolsがありません: ' + base);
  const versions = readdirSync(base).filter(value => /^\d+\.\d+\.\d+$/.test(value))
    .sort((a, b) => b.localeCompare(a, undefined, { numeric: true }));
  const tools = versions.map(version => join(base, version)).find(path =>
    existsSync(join(path, platform === 'win32' ? 'aapt.exe' : 'aapt')) && existsSync(join(path, 'lib/apksigner.jar')));
  if (!tools) throw new Error('aaptとapksignerを含むAndroid build-toolsがありません。');
  const java = join(env.JAVA_HOME || join(player, 'OpenJDK'), 'bin', platform === 'win32' ? 'java.exe' : 'java');
  if (!existsSync(java)) throw new Error('APK署名の検証に使うJavaがありません: ' + java);
  return { aapt: join(tools, platform === 'win32' ? 'aapt.exe' : 'aapt'), java, signer: join(tools, 'lib/apksigner.jar') };
}

export function checkApkReports(badging: string, manifest: string, entries: string, info: { version: string; versionCode: number }) {
  const packageLine = badging.split(/\r?\n/).find(line => line.startsWith('package:')) || '';
  if (!packageLine.includes("name='io.github.maya0513.wheelchairsonification'") ||
      !packageLine.includes(`versionCode='${info.versionCode}'`) || !packageLine.includes(`versionName='${info.version}'`))
    throw new Error('APKのアプリID・版がビルド情報と一致しません。');
  if (/application-debuggable/.test(badging) || /android:debuggable[^\r\n]*0xffffffff/i.test(manifest))
    throw new Error('デバッグ可能なAPKは公開しません。');
  if (!/^native-code: 'arm64-v8a'\s*$/m.test(badging)) throw new Error('APKがARM64専用ではありません。');
  if (!manifest.includes('org.khronos.openxr.intent.category.IMMERSIVE_HMD') ||
      !manifest.includes('org.khronos.openxr.permission.OPENXR') || !manifest.includes('com.htc.ViveOpenXR.SdkVersion'))
    throw new Error('Focus Vision用のOpenXR Manifestを確認できません。');
  const files = new Set(entries.split(/\r?\n/));
  for (const name of ['lib/arm64-v8a/libUnityOpenXR.so', 'lib/arm64-v8a/libopenxr_loader.so',
    'lib/arm64-v8a/libviveopenxr.so', 'assets/Noto-OFL.txt', 'assets/Liberation-OFL.txt', 'assets/HTC-OpenXR-LICENSE.txt'])
    if (!files.has(name)) throw new Error('APKの必須ファイルがありません: ' + name);
}

export function verifyApk(apk: string, editor: string, env: NodeJS.ProcessEnv, info: { version: string; versionCode: number }, run = spawnSync) {
  const tools = androidTools(editor, env);
  const capture = (command: string, args: string[]) => {
    const result = run(command, args, { env, encoding: 'utf8', shell: false, maxBuffer: 16 * 1024 * 1024 });
    if (result.status !== 0) throw new Error(`APK検証が失敗しました: ${args[0]}。Android SDKと署名・成果物を確認してください。`);
    return result.stdout;
  };
  capture(tools.java, ['-jar', tools.signer, 'verify', '--verbose', apk]);
  checkApkReports(capture(tools.aapt, ['dump', 'badging', apk]),
    capture(tools.aapt, ['dump', 'xmltree', apk, 'AndroidManifest.xml']), capture(tools.aapt, ['list', apk]), info);
  return { signatureVerified: true, arm64Only: true, debuggable: false, xrManifestVerified: true,
    nativeLibrariesVerified: true, licenseFilesVerified: true, deviceInstallationTested: false };
}
