import test from 'node:test';
import assert from 'node:assert/strict';
import { paneIds, removePane, splitPane, updateSplitRatio } from '../src/terminal-split-tree.js';

test('splits replace only the requested pane and support both axes', () => {
  const initial = { type: 'pane', id: 'a' };
  const horizontal = splitPane(initial, 'a', 'b', 'row', 'split-1');
  const nested = splitPane(horizontal, 'b', 'c', 'column', 'split-2');
  assert.deepEqual(paneIds(nested), ['a', 'b', 'c']);
  assert.equal(nested.first.id, 'a');
  assert.equal(nested.second.direction, 'column');
  assert.strictEqual(splitPane(nested, 'missing', 'd', 'row', 'split-3'), nested);
  assert.strictEqual(splitPane(nested, 'a', 'a', 'row', 'split-4'), nested);
});

test('closing a pane collapses empty splits without moving unrelated leaves', () => {
  const initial = { type: 'pane', id: 'a' };
  const horizontal = splitPane(initial, 'a', 'b', 'row', 'split-1');
  const nested = splitPane(horizontal, 'b', 'c', 'column', 'split-2');
  const withoutB = removePane(nested, 'b');
  assert.deepEqual(paneIds(withoutB), ['a', 'c']);
  assert.equal(withoutB.id, 'split-1');
  const survivor = removePane(withoutB, 'a');
  assert.deepEqual(paneIds(survivor), ['c']);
  assert.strictEqual(removePane(survivor, 'missing'), survivor);
  assert.equal(removePane(survivor, 'c'), null);
});

test('split ratios stay within usable pane bounds', () => {
  const split = splitPane({ type: 'pane', id: 'a' }, 'a', 'b', 'row', 'split-1');
  assert.equal(updateSplitRatio(split, 'split-1', 0.93).ratio, 0.85);
  assert.equal(updateSplitRatio(split, 'split-1', 0.03).ratio, 0.15);
  assert.strictEqual(updateSplitRatio(split, 'missing', 0.7), split);
});
