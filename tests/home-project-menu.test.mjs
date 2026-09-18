import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const app = await readFile(new URL('../src/app.js', import.meta.url), 'utf8');
const css = await readFile(new URL('../src/styles.css', import.meta.url), 'utf8');

test('home projects use a context menu instead of a remove control', () => {
  assert.doesNotMatch(app, /home-project-remove/);
  assert.doesNotMatch(css, /home-project-remove/);
  assert.match(app, /showProjectMenu/);
  assert.match(app, /'ContextMenu'/);
  assert.match(app, /event\.shiftKey && event\.key === 'F10'/);
  assert.match(app, /작업 공간 열기/);
  assert.match(app, /프로젝트 이름 편집/);
  assert.match(app, /프로젝트 제거/);
  assert.match(app, /setProjectMenuPaused\(path, true\)/);
  assert.match(app, /setProjectMenuPaused\(projectMenu\.dataset\.workspace, false\)/);
  assert.match(app, /closeProjectMenuOnOutsidePointer\(event\) \{ if \(projectMenu && !projectMenu\.contains\(event\.target\)\) closeProjectMenu\(\); \}/);
  assert.match(app, /removeEventListener\('pointerdown', closeProjectMenuOnOutsidePointer, true\)/);
  assert.match(css, /\.home-project-dot\.menu-open,\.home-project-label\.menu-open\{animation-play-state:paused\}/);
});

test('project aliases remain display-only settings', () => {
  assert.match(app, /projectNames/);
  assert.match(app, /프로젝트 폴더나 경로는 바뀌지 않습니다/);
  assert.match(css, /home-project-label-layer/);
  assert.match(css, /home-project-menu\{[^}]*z-index:9/);
  assert.doesNotMatch(css, /\.home-window-controls button\{width:/);
  assert.match(css, /\.home-window-controls\{background:transparent/);
  assert.match(css, /\.topbar,\.home-header\{height:43px\}/);
  assert.match(css, /\.home-header \.window-controls\{align-self:stretch;margin-left:-10px\}/);
  assert.match(app, /home-header-actions[^]*home-pick-folder[^]*window-controls home-window-controls/);
});
