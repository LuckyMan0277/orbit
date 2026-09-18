// Older shells treat a pasted newline as Enter. Keep pasting local and safe
// by joining lines unless the active shell opted into bracketed paste.
export function prepareTerminalPaste(value, bracketedPaste = false) {
  const text = String(value ?? '').replace(/\r\n?/g, '\n');
  if (!text.includes('\n') || bracketedPaste) return { text, collapsed: false };
  return { text: text.replace(/\n+/g, ' '), collapsed: true };
}
