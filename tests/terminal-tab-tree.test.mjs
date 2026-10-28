import test from 'node:test';
import assert from 'node:assert/strict';
import { activateTab, addTab, dockTab, findGroup, groupIds, removeTab, reorderTab, splitCorner, splitGroup, tabIds } from '../src/terminal-tab-tree.js';

const one = () => ({ type: 'group', id: 'g1', tabs: ['a'], activeTabId: 'a' });
const ids = (prefix = 'x') => ({ groupId: `${prefix}g`, splitId: `${prefix}s` });

test('groups add, select, reorder and close tabs while preserving the active tab', () => {
  let tree = addTab(one(), 'g1', 'b');
  assert.deepEqual(findGroup(tree, 'g1').tabs, ['a', 'b']);
  assert.equal(findGroup(tree, 'g1').activeTabId, 'b');
  tree = activateTab(tree, 'g1', 'a');
  tree = reorderTab(tree, 'g1', 'a', 1);
  assert.deepEqual(findGroup(tree, 'g1').tabs, ['b', 'a']);
  assert.equal(findGroup(tree, 'g1').activeTabId, 'a');
  tree = removeTab(tree, 'a');
  assert.deepEqual(findGroup(tree, 'g1').tabs, ['b']);
  assert.equal(findGroup(tree, 'g1').activeTabId, 'b');
});

test('moving a tab merges groups and moving the last tab collapses its split', () => {
  const split = splitGroup(one(), 'g1', 'g2', 'b', 'row', 's1');
  const moved = dockTab(split, 'g2', 'b', 'g1', 'center', ids());
  assert.deepEqual(groupIds(moved), ['g1']);
  assert.deepEqual(findGroup(moved, 'g1').tabs, ['a', 'b']);
  assert.equal(findGroup(moved, 'g1').activeTabId, 'b');
  assert.deepEqual(tabIds(moved), ['a', 'b']);
});

test('cross-group center docking can insert before the target tab', () => {
  const split = splitGroup(one(), 'g1', 'g2', 'b', 'row', 's1');
  const moved = dockTab(split, 'g1', 'a', 'g2', 'center', ids(), 0);
  assert.deepEqual(groupIds(moved), ['g2']);
  assert.deepEqual(findGroup(moved, 'g2').tabs, ['a', 'b']);
  assert.equal(findGroup(moved, 'g2').activeTabId, 'a');
});

test('edge docking preserves tab sessions and self-drop of a lone tab is a no-op', () => {
  const split = splitGroup(one(), 'g1', 'g2', 'b', 'row', 's1');
  const next = dockTab(split, 'g2', 'b', 'g1', 'top', ids());
  assert.deepEqual(tabIds(next), ['b', 'a']);
  assert.equal(next.direction, 'column');
  const lone = one();
  assert.strictEqual(dockTab(lone, 'g1', 'a', 'g1', 'left', ids()), lone);
});

test('corner docking makes a quarter group and an empty adjacent group without creating tabs', () => {
  const split = splitGroup(one(), 'g1', 'g2', 'b', 'row', 's1');
  const next = splitCorner(split, 'g1', 'g2', 'a', 'top-right', {
    emptyGroupId: 'empty', innerSplitId: 'inner', tabGroupId: 'tab-group', outerSplitId: 'outer',
  });
  assert.deepEqual(tabIds(next), ['b', 'a']);
  assert.deepEqual(groupIds(next), ['g2', 'tab-group', 'empty']);
  assert.deepEqual(findGroup(next, 'empty').tabs, []);
  assert.equal(next.direction, 'row');
  assert.equal(next.second.direction, 'column');
  const lone = one();
  assert.strictEqual(splitCorner(lone, 'g1', 'g1', 'a', 'top-left', {
    emptyGroupId: 'e', innerSplitId: 'i', tabGroupId: 't', outerSplitId: 'o',
  }), lone);
});
