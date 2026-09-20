// Which desktop the UI is running on. Windows behaviour is the default; the macOS host reports
// itself the same way the browser does.
export const isMac = /^Mac/i.test(navigator.platform || '') || /Macintosh/.test(navigator.userAgent || '');
if (isMac) document.documentElement.dataset.platform = 'mac';

// Shells the "new terminal" choices offer. macOS has one: the user's login shell.
export const shellProfiles = isMac ? ['powershell'] : ['powershell', 'cmd'];

// The command key on a Mac, Ctrl elsewhere. Ctrl stays free for the terminal (Ctrl+C is an interrupt).
export const hasModifier = event => (isMac ? event.metaKey : event.ctrlKey);

export function joinPath(folder, name) {
  const separator = folder.includes('\\') && !folder.includes('/') ? '\\' : '/';
  return `${folder.replace(/[\\/]$/, '')}${separator}${name}`;
}
