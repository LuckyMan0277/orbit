import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const app = await readFile(new URL('../src/app.js', import.meta.url), 'utf8');
const topbar = app.match(/<header class="topbar">[^]*?<\/header>/)[0];

test('the workspace top bar keeps only what is needed at a glance', () => {
  assert.doesNotMatch(topbar, /terminal-mini|session-count/);
  assert.doesNotMatch(topbar, /작업 공간/);
  assert.doesNotMatch(topbar, /id="home-return"/);
  assert.doesNotMatch(topbar, /외부 연결<\/span>|명령 찾기 <kbd>/);
  assert.match(topbar, /id="breadcrumb-folder"/);
  assert.match(topbar, /id="sidebar-toggle"/);
});

test('the remaining top bar buttons are icon-only and keep an accessible name', () => {
  assert.match(topbar, /id="external-connection"[^>]*aria-label="기기 연결"/);
  assert.match(topbar, /id="command-button"[^>]*aria-label="명령 찾기"/);
  assert.match(topbar, /class="secrets-open"[^>]*aria-label="API 키 보관함"/);
  assert.match(topbar, /class="window-controls"/);
});

test('only split view stays among the workspace actions; search and paste live in the command palette', () => {
  const actions = app.match(/\$\('#workspace-actions'\)\.append\(([^]*?)\);/)[1];
  assert.match(actions, /iconButton\('split'/);
  assert.doesNotMatch(actions, /iconButton\('search'|iconButton\('clipboard'/);
  assert.match(app, /\['터미널에서 찾기','Ctrl Shift F',terminalSearch\]/);
  assert.match(app, /\['클립보드를 터미널에 붙여넣기','Ctrl V',pasteClipboardToActive\]/);
});

test('the project screen is opened from the sidebar logo', () => {
  assert.match(app, /<button class="brand brand-home" id="home-return"/);
  assert.match(app, /\$\('#home-return'\)\.onclick = showHome;/);
});

test('every function the command palette calls is defined, so Ctrl+K and the top bar button cannot throw', () => {
  const list = app.match(/const actions=\[([^]*?)\];\s*\n?\s*dialog\('명령 찾기'/)?.[1] ?? '';
  assert.ok(list.length > 200, 'could not find the command palette action list');
  const names = new Set([...list.matchAll(/,\s*(?:\(\)=>)?\s*([A-Za-z_$][\w$]*)\s*(?:\([^)]*\))?\s*\]/g)].map(match => match[1]));
  assert.ok(names.size >= 8, 'expected several palette actions, found ' + [...names].join(','));
  for (const name of names) {
    if (['newTerminal', 'newSessionDialog'].includes(name)) continue;
    assert.match(app, new RegExp('function ' + name + '\\b|const ' + name + '\\b|let ' + name + '\\b'), 'command palette calls ' + name + ' but it is not defined');
  }
});
