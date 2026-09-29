import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const app = await readFile(new URL('../src/app.js', import.meta.url), 'utf8');
const program = await readFile(new URL('../native/Program.cs', import.meta.url), 'utf8');
const vault = await readFile(new URL('../native/SecretVault.cs', import.meta.url), 'utf8');

test('projects are followed by folder ID after a rename or move', () => {
  assert.match(program, /case "resolveProjects"/);
  assert.match(program, /ProjectLocator\.Resolve/);
  assert.match(app, /call\('resolveProjects'/);
  assert.match(app, /state\.settings\.projectIds/);
  // Checked on start, on Home, before opening a project and before the phone lists projects.
  assert.match(app, /renderHomeOrbit\(\);\r?\n  syncProjectLocations\(\);/);
  assert.match(app, /path = await locateProject\(path\)/);
  assert.match(app, /on\('remoteProjects', async data => \{ await syncProjectLocations\(\)/);
});

test('a relocated project keeps its name and API keys', () => {
  assert.match(app, /state\.settings\.projectNames\[toKey\] \?\?= name/);
  assert.match(app, /call\('moveProject', \{ from, to \}\)/);
  assert.match(vault, /public bool MoveProject\(string from,string to\)/);
  assert.match(program, /case "moveProject": if\(owner!=null\)throw/);
  assert.match(app, /폴더 위치 변경/);
});
