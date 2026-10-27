export function blocksWorkspaceShortcut(event, enabled = true) {
  if (!enabled || !event.ctrlKey) return false;
  if (!event.altKey && ['KeyO', 'KeyS', 'KeyK', 'KeyP', 'KeyB'].includes(event.code)) return true;
  return event.shiftKey && ['KeyT', 'KeyW', 'KeyD', 'KeyF'].includes(event.code);
}
