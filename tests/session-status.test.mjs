import test from 'node:test';
import assert from 'node:assert/strict';
import { sessionStatus, groupByProject, sessionName, sessionLabel } from '../src/session-status.js';

const base = { exited: false, asking: false, now: 10_000, lastOutputAt: 0, unread: false };

test('output that is still flowing means the session is working', () => {
  assert.equal(sessionStatus({ ...base, lastOutputAt: 9_000 }).key, 'working');
  assert.equal(sessionStatus({ ...base, lastOutputAt: 9_000 }).label, '작업 중');
});

test('a quiet session is idle, and a hidden one that produced output wants a look', () => {
  assert.equal(sessionStatus({ ...base, lastOutputAt: 1_000 }).key, 'idle');
  assert.equal(sessionStatus({ ...base }).key, 'idle');
  assert.deepEqual(sessionStatus({ ...base, lastOutputAt: 1_000, unread: true }), { key: 'attention', label: '확인해 보세요' });
});

test('output still flowing beats the unread mark, so a busy session is not reported as finished', () => {
  assert.equal(sessionStatus({ ...base, lastOutputAt: 9_500, unread: true }).key, 'working');
});

test('a pending key request and an ended session outrank everything else', () => {
  assert.deepEqual(sessionStatus({ ...base, asking: true, lastOutputAt: 9_900 }), { key: 'attention', label: '키 요청' });
  assert.equal(sessionStatus({ ...base, exited: true, asking: true, lastOutputAt: 9_900, unread: true }).key, 'ended');
});

test('sessions group by project in first-seen order, ignoring case', () => {
  const items = [{ p: 'C:\\Work\\orbit', n: 1 }, { p: 'C:\\Work\\app', n: 2 }, { p: 'c:\\work\\ORBIT', n: 3 }];
  const groups = groupByProject(items, item => item.p);
  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].items.map(item => item.n), [1, 3]);
  assert.equal(groups[0].path, 'C:\\Work\\orbit');
  assert.deepEqual(groups[1].items.map(item => item.n), [2]);
});

test('a session without a project still groups', () => {
  assert.equal(groupByProject([{ p: '' }, { p: undefined }], item => item.p).length, 1);
});

test('names carry the project so two projects\' Claude tabs differ, and count up inside one project', () => {
  assert.equal(sessionName('orbit', 'Claude', 0), 'orbit · Claude');
  assert.equal(sessionName('orbit', 'Claude', 1), 'orbit · Claude 2');
  assert.equal(sessionName('my-app', 'Claude', 0), 'my-app · Claude');
  assert.equal(sessionName('', 'Codex', 0), 'Codex');
  assert.equal(sessionLabel('Codex', 0), 'Codex');
  assert.equal(sessionLabel('Codex', 2), 'Codex 3');
});
