import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { closeSessions, collectSessions, startServer, syncUnityPackage } from './unity-mcp.mts';

const first = '0123456789abcdef0123456789abcdef';
const second = 'abcdef0123456789abcdef0123456789';

test('Unity package follows the resolved stable server without changing other dependencies', (t) => {
  const root = mkdtempSync(join(tmpdir(), 'unity mcp sync '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const directory = join(root, 'wheelchair-sonification/Packages');
  mkdirSync(directory, { recursive: true });
  const path = join(directory, 'manifest.json');
  const manifest = {
    dependencies: { 'com.coplaydev.unity-mcp': 'old', 'com.unity.inputsystem': '1.19.0' },
  };
  writeFileSync(path, JSON.stringify(manifest, null, 2) + '\n');
  assert.equal(syncUnityPackage(root, '10.3.0'), true);
  const updated = readFileSync(path, 'utf8');
  const dependencies = JSON.parse(updated).dependencies;
  assert.equal(dependencies['com.unity.inputsystem'], '1.19.0');
  assert.equal(
    dependencies['com.coplaydev.unity-mcp'],
    'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.3.0',
  );
  const modified = statSync(path).mtimeMs;
  assert.equal(syncUnityPackage(root, '10.3.0'), false);
  assert.equal(statSync(path).mtimeMs, modified);
  assert.throws(() => syncUnityPackage(root, 'latest'), /バージョン/);
  assert.throws(() => syncUnityPackage(root, '10.4.0b1'), /バージョン/);
  assert.equal(readFileSync(path, 'utf8'), updated);
});

test('a failed server preserves its original exit code', async () => {
  assert.equal(
    await startServer(process.execPath, ['-e', 'process.exit(7)'], 'http://127.0.0.1:8080/mcp'),
    7,
  );
});

test('only HTTP sessions from the SDK log are collected and deduplicated', () => {
  const sessions = new Set<string>();
  for (const line of [
    `2026-10-02 - mcp.server.streamable_http_manager - INFO - Created new transport with session ID: ${first}`,
    `mcp.server.streamable_http_manager - INFO - Created new transport with session ID: ${first}`,
    `Plugin session ${second} registered`,
  ])
    collectSessions(line, sessions);
  assert.deepEqual([...sessions], [first]);
});

test('shutdown uses DELETE, accepts already closed sessions and includes new arrivals', async (t) => {
  const sessions = new Set([first]);
  const requests: string[] = [];
  const server = createServer((request, response) => {
    assert.equal(request.method, 'DELETE');
    assert.equal(request.url, '/mcp');
    const id = String(request.headers['mcp-session-id']);
    requests.push(id);
    if (id === first) sessions.add(second);
    response.writeHead(id === first ? 200 : 404).end();
  });
  await new Promise<void>((done) => server.listen(0, '127.0.0.1', done));
  t.after(() => {
    server.closeAllConnections();
    server.close();
  });
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  await closeSessions(`http://127.0.0.1:${address.port}/mcp`, sessions);
  assert.deepEqual(requests, [first, second]);
});

test('HTTP errors and a hanging DELETE are reported instead of treated as shutdown success', async (t) => {
  const server = createServer((request, response) => {
    if (request.url === '/error') response.writeHead(503).end();
  });
  await new Promise<void>((done) => server.listen(0, '127.0.0.1', done));
  t.after(() => {
    server.closeAllConnections();
    server.close();
  });
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  const endpoint = `http://127.0.0.1:${address.port}`;
  await assert.rejects(closeSessions(endpoint + '/error', new Set([first])), /HTTP 503/);
  await assert.rejects(closeSessions(endpoint + '/hang', new Set([first]), 100), /abort|timeout/i);
});
