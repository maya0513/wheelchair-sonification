import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const validator = join(dirname(fileURLToPath(import.meta.url)), 'unity-results.py');
const python = process.env.UNITY_RESULTS_PYTHON || 'python3';
const successful = '<test-run total="1" passed="1" failed="0" skipped="0" inconclusive="0" result="Passed"><test-suite><test-case fullname="test" result="Passed" /></test-suite></test-run>';

test('only a complete nonempty passing NUnit report succeeds', t => {
  const root = mkdtempSync(join(tmpdir(), 'unity-results-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const path = join(root, 'results.xml');
  for (const [label, xml, expected] of [
    ['success', successful, 0],
    ['missing', null, 1],
    ['empty', '', 1],
    ['malformed', '<test-run>', 1],
    ['wrong root', '<report/>', 1],
    ['zero tests', '<test-run result="Passed" total="0" passed="0"/>', 1],
    ['failed', successful.replaceAll('Passed', 'Failed'), 1],
    ['skipped leaf', successful.replace('result="Passed" />', 'result="Skipped" />'), 1],
    ['inconsistent counts', successful.replace('total="1"', 'total="2"'), 1],
    ['missing cases', successful.replace('<test-case fullname="test" result="Passed" />', ''), 1],
    ['non-numeric count', successful.replace('total="1"', 'total="invalid"'), 1],
  ] as const) {
    rmSync(path, { force: true });
    if (xml !== null) writeFileSync(path, xml);
    const result = spawnSync(python, [validator, path], { encoding: 'utf8', timeout: 5000 });
    assert.equal(result.status, expected, `${label}: ${result.stderr}`);
  }
});
