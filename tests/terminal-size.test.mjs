import test from 'node:test';
import assert from 'node:assert/strict';
import { terminalSize } from '../src/terminal-size.js';

test('Windows terminal grid matches native bounds at tiny and large pane sizes', () => {
  assert.deepEqual(terminalSize({ cols: 2, rows: 1 }), { cols: 10, rows: 3 });
  assert.deepEqual(terminalSize({ cols: 800, rows: 600 }), { cols: 500, rows: 300 });
  assert.deepEqual(terminalSize({ cols: 97, rows: 25 }), { cols: 97, rows: 25 });
});
test('Mac retains its fit addon grid', () => {
  assert.deepEqual(terminalSize({ cols: 2, rows: 1 }, false), { cols: 2, rows: 1 });
});
