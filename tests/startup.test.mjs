import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const app = await readFile(new URL('../src/app.js', import.meta.url), 'utf8');
const program = await readFile(new URL('../native/Program.cs', import.meta.url), 'utf8');
const startup = await readFile(new URL('../native/Startup.cs', import.meta.url), 'utf8');
const installer = await readFile(new URL('../installer/orbit.iss', import.meta.url), 'utf8');

test('Orbit can be set to start with Windows from settings', () => {
  assert.match(app, /Windows 시작 시 Orbit 실행/);
  assert.match(app, /call\('startup',\{enabled:startupCheck\.checked\}\)/);
  assert.match(app, /startup\.hidden=!isDesktop\|\|isMac/);
  assert.match(program, /case "startup": if\(owner!=null\)throw/);
  // Tests never write the real Run key.
  assert.match(program, /!uiTest&&!remoteTest\)Startup\.Set/);
  assert.match(startup, /Software\\Microsoft\\Windows\\CurrentVersion\\Run/);
  assert.match(startup, /--startup/);
});

test('a startup launch is minimized, has a sane folder and is removed on uninstall', () => {
  assert.match(program, /if\(atStartup\)WindowState=FormWindowState\.Minimized/);
  assert.match(program, /atStartup \? Path\.GetDirectoryName\(Application\.ExecutablePath\)/);
  assert.match(installer, /ValueName: "Orbit"; Flags: uninsdeletevalue dontcreatekey/);
});
