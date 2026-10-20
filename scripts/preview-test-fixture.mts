import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
export function validPreviewFixture(directory: string, commit = 'a'.repeat(40)) {
  mkdirSync(directory, { recursive: true });
  const png = Buffer.alloc(128);
  Buffer.from('89504e470d0a1a0a', 'hex').copy(png);
  png.write('IHDR', 12); png.writeUInt32BE(1440, 16); png.writeUInt32BE(1440, 20);
  // Only file validation is exercised here; actual Unity renders are tested separately.
  writeFileSync(join(directory, 'screen.png'), png);
  writeFileSync(join(directory, 'contact-sheet.png'), png);
  const manifest = { passed: true, physical_device_measured: false, unity_version: '6000.6.4f1', source_commit: commit,
    source_dirty: false, errors: [], shots: Array.from({ length: 20 }, (_, i) => ({ name: 'shot-' + i,
      file: 'screen.png', title: '画面 ' + i, assumed_vertical_fov_degrees: 90, light_background: false,
      intentional_world_panel_offscreen: false })) };
  writeFileSync(join(directory, 'manifest.json'), JSON.stringify(manifest));
  writeFileSync(join(directory, 'index.html'), '<html>preview</html>');
  return manifest;
}
