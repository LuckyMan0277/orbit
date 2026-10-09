export function terminalSize(size, windows = true) {
  return windows ? { cols: Math.max(10, Math.min(500, size.cols)), rows: Math.max(3, Math.min(300, size.rows)) } : size;
}
