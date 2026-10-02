import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { test } from 'node:test';
import { closeSessions, collectSessions, startServer } from './unity-mcp.mts';

const first = '0123456789abcdef0123456789abcdef';
const second = 'abcdef0123456789abcdef0123456789';

test('a failed server preserves its original exit code', async () => {
  assert.equal(await startServer(process.execPath, ['-e', 'process.exit(7)'], 'http://127.0.0.1:8080/mcp'), 7);
});

test('only HTTP sessions from the pinned SDK log are collected and deduplicated', () => {
  const sessions = new Set<string>();
  for (const line of [
    `2026-10-02 - mcp.server.streamable_http_manager - INFO - Created new transport with session ID: ${first}`,
    `mcp.server.streamable_http_manager - INFO - Created new transport with session ID: ${first}`,
    `Plugin session ${second} registered`,
  ]) collectSessions(line, sessions);
  assert.deepEqual([...sessions], [first]);
});

test('shutdown uses DELETE, accepts already closed sessions and includes new arrivals', async t => {
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
  await new Promise<void>(done => server.listen(0, '127.0.0.1', done));
  t.after(() => { server.closeAllConnections(); server.close(); });
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  await closeSessions(`http://127.0.0.1:${address.port}/mcp`, sessions);
  assert.deepEqual(requests, [first, second]);
});

test('HTTP errors and a hanging DELETE are reported instead of treated as shutdown success', async t => {
  const server = createServer((request, response) => {
    if (request.url === '/error') response.writeHead(503).end();
  });
  await new Promise<void>(done => server.listen(0, '127.0.0.1', done));
  t.after(() => { server.closeAllConnections(); server.close(); });
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  const endpoint = `http://127.0.0.1:${address.port}`;
  await assert.rejects(closeSessions(endpoint + '/error', new Set([first])), /HTTP 503/);
  await assert.rejects(closeSessions(endpoint + '/hang', new Set([first]), 100), /abort|timeout/i);
});
