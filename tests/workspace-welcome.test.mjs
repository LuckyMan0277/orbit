import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [app, css] = await Promise.all([
  readFile(new URL('../src/app.js', import.meta.url), 'utf8'),
  readFile(new URL('../src/styles.css', import.meta.url), 'utf8')
]);

test('workspace entry keeps the terminal welcome without a project-orbit surface', () => {
  assert.doesNotMatch(app, /class="welcome project-home"/);
  assert.doesNotMatch(app, /class="project-orbit"|id="project-orbit"|renderProjectOrbit/);
  assert.doesNotMatch(app, /orbit-art|orbit-ring|ring-one|ring-two|orbit-satellite/);
  assert.doesNotMatch(css, /\.project-home\{|\.project-orbit\{|\.project-star\{|@keyframes project-orbit/);
  assert.doesNotMatch(css, /\.orbit-art\{|\.orbit-ring\{|\.ring-one\{|\.ring-two\{|\.orbit-core\{|\.orbit-satellite\{/);
  assert.match(app, /class="welcome-copy workspace-welcome-copy"/);
  assert.match(app, /class="workspace-launch-section"/);
  assert.match(app, /id="terminal-layout" class="terminal-layout"/);
  assert.match(css, /\.workspace-launch-section\{/);
});

test('project orbit is retained exclusively by the Home screen', () => {
  assert.match(app, /id="home-orbit"/);
  assert.match(app, /function renderHomeOrbit\(\)/);
  assert.match(css, /\.home-project-orbit\{/);
});
