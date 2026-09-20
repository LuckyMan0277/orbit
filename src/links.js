// Web addresses without a scheme. Bare domains are limited to common TLDs so file names such as main.py or notes.md stay files.
const webHost = /www\.[\w-]+(?:\.[\w-]+)+|localhost(?![\w.-])|(?:\d{1,3}\.){3}\d{1,3}(?![\w.-])|(?:[a-z0-9-]+\.)+(?:com|net|org|io|dev|app|ai|kr|info|edu|gov|xyz|tv|gg)(?![\w-])/.source;
const webTail = /(?::\d{1,5})?(?:[/?#][^\s<>"'`]*)?/.source;
const webAddress = `(?<![\\w@.\\\\/-])(?:${webHost})${webTail}`;
const webAddressOnly = new RegExp(`^(?:${webHost})${webTail}$`, 'i');
const linkPattern = new RegExp(/https?:\/\/[^\s<>"`]+|file:\/\/[^\s<>"`]+/.source + '|' + webAddress + '|' + /"[^"\r\n]+"|'[^'\r\n]+'|(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|[\\/])[^\s<>"'`|]+|(?:[\w.@-]+[\\/])+[\w.@()\[\]-]+(?::\d+(?::\d+)?)?|[\w.-]+\.[A-Za-z0-9]{1,12}(?::\d+(?::\d+)?)?/.source, 'gu');
// Return visible token offsets, preserving line/column suffixes and quoted paths.
export function extractLinks(text) {
  const links = [];
  for (const m of text.matchAll(linkPattern)) {
    let value = m[0], start = m.index;
    if (text[start - 1] === '@') continue; // the domain part of an e-mail address
    if (/^["']/.test(value)) { value = value.slice(1, -1); start++; }
    value = value.replace(/[.,;!?]+$/, '');
    while (value.endsWith(')') && (value.match(/\)/g)?.length || 0) > (value.match(/\(/g)?.length || 0)) value = value.slice(0, -1);
    if (/^(https?:|file:|[A-Za-z]:[\\/]|\.{0,2}[\\/])/.test(value) || /[\\/]|\.[A-Za-z0-9]/.test(value) || webAddressOnly.test(value)) links.push({ value, start, end: start + value.length });
  }
  return links;
}
export function parseLocation(value) {
  if (/^https?:\/\//i.test(value)) return { kind: 'url', path: value };
  // localhost and IP addresses are usually local dev servers without TLS.
  if (webAddressOnly.test(value)) return { kind: 'url', path: `${/^(?:localhost|\d)/i.test(value) ? 'http' : 'https'}://${value}` };
  if (/^file:\/\//i.test(value)) {
    const url = new URL(value);
    value = (url.hostname ? `\\\\${url.hostname}` : '') + decodeURIComponent(url.pathname).replace(/^\/([A-Za-z]:)/, '$1');
  } else try { value = decodeURIComponent(value); } catch { }
  value = value.replace(/^\/([A-Za-z]:[\\/])/, '$1');
  const match = value.match(/:(\d+)(?::(\d+))?$/);
  return { kind: 'path', path: match ? value.slice(0, match.index) : value, line: match ? +match[1] : 1, column: match?.[2] ? +match[2] : 1 };
}
