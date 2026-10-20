import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { createPreviewArchive, gallery, validatePreview } from './unity-preview.mts';
import { validPreviewFixture } from './preview-test-fixture.mts';
import { snapshotUnitySettings } from './unity-state.mts';
function fixture(t: { after: (fn: () => void) => void }) {
  const directory = mkdtempSync(join(tmpdir(), 'xr preview '));
  t.after(() => rmSync(directory, { recursive: true, force: true })); return directory;
}
test('gallery links real PNG crops, escapes metadata and states optical limits', t => {
  const d = fixture(t); const m = validPreviewFixture(d);
  m.shots[0].title = '<script>wrong</script>';
  const html = gallery(m);
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>wrong/);
  assert.match(html, /src="screen.png"/); assert.match(html, /レンズ越し/);
  validatePreview(d, m.source_commit);
  assert.throws(() => validatePreview(d, 'b'.repeat(40)), /一致/);
  writeFileSync(join(d, 'screen.png'), Buffer.alloc(128));
  assert.throws(() => validatePreview(d), /PNG形式/);
});
test('failed, empty or path escaping captures cannot be accepted', t => {
  const d = fixture(t); const m = validPreviewFixture(d);
  for (const mutation of [() => { m.passed = false; }, () => { m.shots = []; },
    () => { m.shots[0].file = '../secret.png'; }]) {
    Object.assign(m, validPreviewFixture(d)); mutation();
    writeFileSync(join(d, 'manifest.json'), JSON.stringify(m));
    assert.throws(() => validatePreview(d));
  }
});
test('preview ZIP excludes generated log fixtures and includes standalone viewer', t => {
  const d = fixture(t); validPreviewFixture(d);
  mkdirSync(join(d, 'fixture-research')); writeFileSync(join(d, 'fixture-research/poses.csv'), 'private');
  writeFileSync(join(d, 'private.csv'), 'private');
  const zip = join(d, 'output.zip'); createPreviewArchive(d, zip);
  const r = spawnSync('python3', ['-c', 'import sys,zipfile;print("\\n".join(zipfile.ZipFile(sys.argv[1]).namelist()))', zip], { encoding: 'utf8' });
  assert.equal(r.status, 0); assert.match(r.stdout, /index.html/); assert.match(r.stdout, /screen.png/);
  assert.doesNotMatch(r.stdout, /private|fixture-research|csv/);
});
test('Unity serialization is restored for settings and dynamic fonts after errors', t => {
  const d = fixture(t);
  const font = join(d, 'Assets/Experiment/Resources/XrJapanese.asset');
  mkdirSync(join(d, 'Assets/Experiment/Resources'), { recursive: true }); writeFileSync(font, 'original\r\n');
  const restore = snapshotUnitySettings(d); writeFileSync(font, 'mutated'); restore();
  assert.equal(readFileSync(font, 'utf8'), 'original\r\n');
});
