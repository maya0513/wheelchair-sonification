import { existsSync, lstatSync, readFileSync, readdirSync, rmdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// Unity package hooks and dynamic fonts can serialize assets during read-only previews/builds.
export function snapshotUnitySettings(project: string) {
  const originals = new Map<string, Buffer>();
  const visit = (directory: string) => {
    if (!existsSync(directory)) return;
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile() && entry.name.endsWith('.asset')) originals.set(path, readFileSync(path));
    }
  };
  for (const relative of ['ProjectSettings', 'Assets/Settings', 'Assets/XR', 'Assets/XRI', 'Assets/VIVE',
    'Assets/Experiment/Resources', 'Assets/TextMesh Pro/Resources']) visit(join(project, relative));
  return () => {
    for (const [path, bytes] of originals) if (!existsSync(path) || !bytes.equals(readFileSync(path))) writeFileSync(path, bytes);
    for (const relative of ['Assets/XR 1', 'Assets/XR/Settings 1']) {
      const path = join(project, relative);
      if (existsSync(path) && lstatSync(path).isDirectory() && readdirSync(path).length === 0) {
        rmdirSync(path); rmSync(path + '.meta', { force: true });
      }
    }
  };
}
