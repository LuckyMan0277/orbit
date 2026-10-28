export const shellProfiles = Object.freeze(['cmd', 'powershell', 'gitbash']);
const labels = Object.freeze({ cmd: 'CMD', powershell: 'PowerShell', gitbash: 'Git Bash' });

export function defaultShellFromSettings(settings) {
  return settings && shellProfiles.includes(settings.defaultShell) ? settings.defaultShell : 'powershell';
}

export function settingsWithDefaultShell(settings, profile) {
  if (!shellProfiles.includes(profile)) throw new Error('Unsupported terminal shell');
  const preserved = settings && typeof settings === 'object' && !Array.isArray(settings) ? settings : {};
  return { ...preserved, defaultShell: profile };
}

export function shellDisplayName(profile) {
  return labels[profile] || labels.powershell;
}
