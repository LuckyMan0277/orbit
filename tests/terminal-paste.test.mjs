import test from 'node:test';
import assert from 'node:assert/strict';
import { prepareTerminalPaste } from '../src/terminal-paste.js';

test('single-line clipboard text stays untouched', () => {
  assert.deepEqual(prepareTerminalPaste('한글 paste', false), { text: '한글 paste', collapsed: false });
});

test('multiline clipboard text cannot submit in a non-bracketed shell', () => {
  assert.deepEqual(prepareTerminalPaste('첫 줄\r\n둘째 줄\n셋째 줄', false), { text: '첫 줄 둘째 줄 셋째 줄', collapsed: true });
});

test('bracketed paste retains multiline text for shells that opt in', () => {
  assert.deepEqual(prepareTerminalPaste('첫 줄\r\n둘째 줄', true), { text: '첫 줄\n둘째 줄', collapsed: false });
});
