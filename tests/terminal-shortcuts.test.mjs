import test from 'node:test';
import assert from 'node:assert/strict';
import { blocksWorkspaceShortcut } from '../src/workspace-shortcuts.js';

test('desktop terminal shortcuts pass through to the shell in terminal-first mode', () => {
  for (const code of ['KeyB', 'KeyK', 'KeyP', 'KeyO', 'KeyS', 'KeyT']) {
    assert.equal(blocksWorkspaceShortcut({ ctrlKey: true, shiftKey: code === 'KeyT', code }, false), false, code);
  }
});

test('the expanded workspace still captures its own shortcut keys', () => {
  for (const code of ['KeyB', 'KeyK', 'KeyP', 'KeyO', 'KeyS']) {
    assert.equal(blocksWorkspaceShortcut({ ctrlKey: true, shiftKey: false, code }, true), true, code);
  }
  for (const code of ['KeyT', 'KeyW', 'KeyD', 'KeyF']) {
    assert.equal(blocksWorkspaceShortcut({ ctrlKey: true, shiftKey: true, code }, true), true, code);
  }
  assert.equal(blocksWorkspaceShortcut({ ctrlKey: false, shiftKey: true, code: 'KeyT' }, true), false);
});
