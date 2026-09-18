import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [app, picker, css, svg, generator] = await Promise.all([
  readFile(new URL('../src/app.js', import.meta.url), 'utf8'),
  readFile(new URL('../src/file-picker.js', import.meta.url), 'utf8'),
  readFile(new URL('../src/styles.css', import.meta.url), 'utf8'),
  readFile(new URL('../assets/orbit.svg', import.meta.url), 'utf8'),
  readFile(new URL('../scripts/generate-icon.ps1', import.meta.url), 'utf8')
]);

test('home and workspace share the asset-backed Orbit wordmark', () => {
  assert.equal((app.match(/class="orbit-wordmark-mark"/g) || []).length, 2);
  assert.match(css, /\.home-brand,\.brand\{[^}]*gap:10px[^}]*font-size:24px/);
  assert.match(css, /\.orbit-wordmark-mark\{[^}]*width:24px[^}]*height:24px/);
});

test('native icon generator and browser asset use the Orbit mark', () => {
  assert.match(svg, /viewBox="0 0 256 256"/);
  assert.match(svg, /ellipse/);
  assert.match(generator, /orbit-256\.png/);
  assert.match(generator, /RotateTransform\(-38\)/);
  assert.match(app, /'home-window-minimize','minimize'.*'home-window-maximize','maximize'.*'home-window-close','close'/s);
});

test('interactive controls use the common SVG icon system instead of text glyphs', () => {
  assert.doesNotMatch(app, /[×☰⌄›▰]/);
  assert.doesNotMatch(picker, />×</);
  assert.match(app, /remove\.append\(icon\('close'\)\)/);
  assert.match(app, /disclosure=icon\('chevronRight'\)/);
  assert.match(picker, /append\(icon\('close'\)\)/);
});
