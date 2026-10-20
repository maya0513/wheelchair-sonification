import { homedir } from 'node:os';
import { join, resolve, win32 } from 'node:path';

export function editorPath(version: string, platform: string, env: NodeJS.ProcessEnv, home = homedir()) {
  if (env.UNITY_EDITOR) return resolve(env.UNITY_EDITOR);
  if (platform === 'win32') return win32.join(env.ProgramFiles || 'C:\\Program Files', 'Unity', 'Hub', 'Editor', version, 'Editor', 'Unity.exe');
  if (platform === 'darwin') return `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`;
  return join(home, 'Unity', 'Hub', 'Editor', version, 'Editor', 'Unity');
}
