import test from 'node:test';
import assert from 'node:assert/strict';
import { getDropPlacement } from '../src/terminal-drop-placement.js';

const rect = (height = 113.25) => ({ left: 100, top: 50, width: 240, height });
const point = (x, y) => ({ x, y });

test('short split cells classify corner drops relative to the terminal body below tabs', () => {
  const bounds = rect(113.25), bodyTop = bounds.top + 30, bodyHeight = bounds.height - 30;
  assert.equal(getDropPlacement(bounds, point(100 + 24, bodyTop + bodyHeight * 0.1)), 'top-left');
  assert.equal(getDropPlacement(bounds, point(100 + 216, bodyTop + bodyHeight * 0.1)), 'top-right');
  assert.equal(getDropPlacement(bounds, point(100 + 24, bodyTop + bodyHeight * 0.9)), 'bottom-left');
  assert.equal(getDropPlacement(bounds, point(100 + 216, bodyTop + bodyHeight * 0.9)), 'bottom-right');
});

test('tab strip stays a center target and body edge bands classify independently', () => {
  const bounds = rect(113.25), bodyTop = bounds.top + 30, bodyHeight = bounds.height - 30;
  assert.equal(getDropPlacement(bounds, point(110, bounds.top + 15)), 'center');
  assert.equal(getDropPlacement(bounds, point(110, bodyTop + bodyHeight / 2)), 'left');
  assert.equal(getDropPlacement(bounds, point(330, bodyTop + bodyHeight / 2)), 'right');
  assert.equal(getDropPlacement(bounds, point(220, bodyTop + bodyHeight * 0.1)), 'top');
  assert.equal(getDropPlacement(bounds, point(220, bodyTop + bodyHeight * 0.9)), 'bottom');
});
