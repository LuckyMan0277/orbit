// Return visible token offsets, preserving line/column suffixes and quoted paths.
export function extractLinks(text) {
  const pattern = /https?:\/\/[^\s<>"`]+|file:\/\/[^\s<>"`]+|"[^"\r\n]+"|'[^'\r\n]+'|(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|[\\/])[^\s<>"'`|]+|(?:[\w.@-]+[\\/])+[\w.@()\[\]-]+(?::\d+(?::\d+)?)?|[\w.-]+\.[A-Za-z0-9]{1,12}(?::\d+(?::\d+)?)?/gu;
  const links = [];
  for (const m of text.matchAll(pattern)) {
    let value = m[0], start = m.index;
    if (/^["']/.test(value)) { value = value.slice(1, -1); start++; }
    value = value.replace(/[.,;!?]+$/, '');
    while (value.endsWith(')') && (value.match(/\)/g)?.length || 0) > (value.match(/\(/g)?.length || 0)) value = value.slice(0, -1);
    if (/^(https?:|file:|[A-Za-z]:[\\/]|\.{0,2}[\\/])/.test(value) || /[\\/]|\.[A-Za-z0-9]/.test(value)) links.push({ value, start, end: start + value.length });
  }
  return links;
}
export function parseLocation(value) {
  if (/^https?:\/\//i.test(value)) return { kind: 'url', path: value };
  if (/^file:\/\//i.test(value)) {
    const url = new URL(value);
    value = (url.hostname ? `\\\\${url.hostname}` : '') + decodeURIComponent(url.pathname).replace(/^\/([A-Za-z]:)/, '$1');
  } else try { value = decodeURIComponent(value); } catch { }
  value = value.replace(/^\/([A-Za-z]:[\\/])/, '$1');
  const match = value.match(/:(\d+)(?::(\d+))?$/);
  return { kind: 'path', path: match ? value.slice(0, match.index) : value, line: match ? +match[1] : 1, column: match?.[2] ? +match[2] : 1 };
}
