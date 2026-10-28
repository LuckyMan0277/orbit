import test from 'node:test';
import assert from 'node:assert/strict';
import { defaultShellFromSettings, settingsWithDefaultShell, shellDisplayName } from '../src/shell-profile.js';

test('new-shell choice defaults to PowerShell and accepts supported persisted profiles', () => {
  assert.equal(defaultShellFromSettings(null), 'powershell');
  assert.equal(defaultShellFromSettings({ defaultShell: 'unknown' }), 'powershell');
  for (const profile of ['cmd', 'powershell', 'gitbash']) assert.equal(defaultShellFromSettings({ defaultShell: profile }), profile);
  assert.equal(shellDisplayName('gitbash'), 'Git Bash');
});

test('changing the default shell preserves other saved application settings', () => {
  const previous = { theme: 'dark', fontSize: 14, recent: ['C:\\Work'], customPreference: { enabled: true } };
  const next = settingsWithDefaultShell(previous, 'gitbash');
  assert.deepEqual(next, { ...previous, defaultShell: 'gitbash' });
  assert.notStrictEqual(next, previous);
  assert.throws(() => settingsWithDefaultShell(previous, 'powershell /c whoami'), /Unsupported/);
});
