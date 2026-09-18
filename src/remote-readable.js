function cellText(cell) {
  if (!cell || cell.getWidth() === 0) return '';
  if (cell.getChars()) return cell.getChars();
  // A zero-code cell inside the visible extent represents cursor positioning
  // (for example tab-like indentation), while trailing zero-code cells are
  // excluded by visibleText below as unused terminal layout padding.
  return cell.getCode() === 0 ? ' ' : String.fromCodePoint(cell.getCode());
}

function visibleText(line) {
  let lastContent = -1;
  for (let x = 0; x < line.length; x++) {
    const cell = line.getCell(x);
    // Code 0 after the last written cell is xterm's unused layout padding.
    // Earlier zero-code cells can be cursor-positioned spaces and stay visible.
    if (cell && cell.getWidth() !== 0 && cell.getCode() !== 0) lastContent = x;
  }
  let text = '';
  for (let x = 0; x <= lastContent; x++) text += cellText(line.getCell(x));
  return text;
}

export function readableLines(buffer, limit = 700) {
  const lines = [];
  const cursor = buffer.baseY + buffer.cursorY;
  let tail = Math.max(0, cursor);
  for (let y = cursor + 1; y < buffer.length; y++) if (buffer.getLine(y)?.translateToString(true)) tail = y;
  for (let y = 0; y < buffer.length && y <= tail; y++) {
    const line = buffer.getLine(y);
    if (!line) continue;
    const text = visibleText(line);
    const next = buffer.getLine(y + 1);
    if (line.isWrapped && lines.length) lines[lines.length - 1] += text;
    else lines.push(text);
    // Readable view omits end-of-logical-line whitespace. Internal and leading
    // explicit spaces survive, including indentation and wrapped code output.
    if (!next?.isWrapped) lines[lines.length - 1] = lines[lines.length - 1].replace(/\s+$/, '');
  }
  return lines.slice(-limit).join('\n');
}

// The remote API has session identity but not CLI metadata. Keep this filter
// intentionally narrow: unrecognized output, prompts, and approvals remain.
export function conversationDetails(buffer, profile, limit = 700) {
  const lines = readableLines(buffer, limit).split('\n');
  if (profile !== 'codex' && profile !== 'claude') return { text:lines.join('\n'), model:'' };
  const hidden = new Set();
  let model = '';
  // A startup title is unambiguous only at the start of a CLI transcript.
  for (let index = 0; index < Math.min(2, lines.length); index++) if (knownTitle(lines[index], profile)) hidden.add(index);
  const tailModel = hideCodexTailChrome(lines, profile, hidden);
  if (tailModel) model = tailModel;
  // Codex puts the completion timer immediately above its live composer. It
  // is deliberately recognized as one tail-anchored layout, rather than by
  // deleting text between independent timer/model-looking lines in output.
  const codexStatusStart = codexStatusBlockStart(lines, profile);
  if (codexStatusStart !== -1) for (let index = codexStatusStart; index < lines.length; index++) {
    const observed = modelName(lines[index]); if (observed) model = observed;
    hidden.add(index);
  }
  // Live CLI chrome is a tail-only construct. A divider plus a known live
  // footer marker lets us remove that compact region without treating task
  // output such as `model:` or `status:` as application metadata.
  const floor = Math.max(0, lines.length - 14);
  // Only consume a contiguous suffix. The divider is the boundary between
  // transcript and a supported CLI footer; never scan past an unknown line.
  let start = lines.length, cues = 0, dividers = 0;
  for (let index = lines.length - 1; index >= floor; index--) {
    const line = lines[index];
    if (divider(line)) { start=index; dividers++; continue; }
    if (!line.trim() || liveFooterMarker(line) || knownCliChrome(line, profile) || codexComposerPlaceholder(line, profile)) { start=index; if (liveFooterMarker(line) || knownCliChrome(line, profile) || codexComposerPlaceholder(line, profile)) cues++; continue; }
    break;
  }
  if (dividers >= 1 && cues >= 2) for (let index = start; index < lines.length; index++) {
    const observed = modelName(lines[index]); if (observed) model = observed;
    hidden.add(index);
  }
  return { text:lines.filter((_, index) => !hidden.has(index)).join('\n').replace(/^\n+|\n+$/g, ''), model };
}

export function conversationText(buffer, profile, limit = 700) { return conversationDetails(buffer, profile, limit).text; }

function knownTitle(line, profile) {
  const value = line.trim();
  return (profile === 'codex' && /^(openai )?codex(?: cli)?(?:\s+v?[\d.]+)?$/i.test(value)) || (profile === 'claude' && /^claude code(?:\s+v?[\d.]+)?$/i.test(value));
}

function knownCliChrome(line, profile) {
  const value = line.trim();
  if (knownTitle(value, profile)) return true;
  if (/^model:\s*(?:gpt[-\w. ]+|claude[-\w. ]+)$/i.test(value)) return true;
  return /^status:\s*(?:ready|working|thinking|idle|waiting|connected)$/i.test(value);
}

// Codex renders this exact placeholder at its live composer. It is accepted
// only while consuming a divider-bounded footer suffix, never as a general
// transcript replacement. Both prompt prefixes are observed terminal forms.
function codexComposerPlaceholder(line, profile) {
  return profile === 'codex' && /^(?:[›>]\s*)?ask codex to do anything$/i.test(line.trim());
}

// A completed Codex turn can leave the composer as either its exact
// placeholder or an empty prompt cursor. A prompt with any entered text is a
// real draft and must remain visible.
function codexLiveComposer(line, profile) {
  return codexComposerPlaceholder(line, profile)
    || codexComposerWithBranch(line, profile)
    || (profile === 'codex' && /^(?:[›❯]\s*)$/.test(line.trim()));
}

function codexBranchStatus(line) {
  return /^main\s*\[\s*default\s*\]$/i.test(line.trim());
}

// Some terminal widths place the branch/status label beside the otherwise
// exact placeholder. It is accepted only as this literal combined chrome.
function codexComposerWithBranch(line, profile) {
  return profile === 'codex' && /^(?:[›>]\s*)?ask codex to do anything\s+main\s*\[\s*default\s*\]$/i.test(line.trim());
}

// Current Codex CLI renders this as a contiguous tail block, for example:
//   Worked for 2m 14s
//   ────────────
//   › Ask Codex to do anything
//   ────────────
//   gpt-5.6-sol low · 82% context left
// Requiring every distinctive part keeps narrative timers, quoted examples,
// code, and a real (non-placeholder) composer draft in the transcript.
function codexStatusBlockStart(lines, profile) {
  if (profile !== 'codex') return -1;
  let end = lines.length - 1;
  while (end >= 0 && !lines[end].trim()) end--;
  if (end < 0 || !codexStatusFooter(lines[end])) return -1;
  const floor = Math.max(0, end - 10);
  for (let start = end - 1; start >= floor; start--) {
    if (!workedForLine(lines[start])) continue;
    const block = lines.slice(start, end + 1);
    const composer = block.findIndex(line => codexLiveComposer(line, profile));
    if (composer === -1 || !block.some(divider)) continue;
    if (block.every(codexStatusChromeLine)) return start;
  }
  return -1;
}

function workedForLine(line) {
  return /^(?:[•●]\s*)?worked\s+for\s+(?:\d+(?:\.\d+)?\s*(?:ms|s|sec(?:onds?)?|m|min(?:utes?)?|h|hours?)\s*)+$/i.test(line.trim());
}

function codexStatusFooter(line) {
  const value = line.trim();
  return /^gpt-[\w.-]+\s+(?:low|medium|high|xhigh|max|ultra)\b/i.test(value)
    && (/(?:\b(?:context|tokens?)\b.*\b(?:left|remaining)\b)|(?:\b\d{1,3}%\s+(?:context\s+)?(?:left|remaining)\b)/i.test(value));
}

function codexStatusChromeLine(line) {
  const value = line.trim();
  return !value || divider(line) || workedForLine(line) || codexStatusFooter(line)
    || codexBranchStatus(line)
    || codexLiveComposer(line, 'codex')
    || /^(?:esc to interrupt|ctrl\+c to interrupt|press esc to|\/ for commands|\? for (?:shortcuts|help)|thinking\.\.\.|[›❯]\s*)$/i.test(value);
}

// Terminal redraws can split the completed-turn footer into separate rows.
// Once an exact Codex composer/branch anchor is present near the tail, hide
// only individually recognized chrome rows. Never remove the text between
// them, so assistant output and approvals survive a changed terminal layout.
function hideCodexTailChrome(lines, profile, hidden) {
  if (profile !== 'codex') return '';
  const floor = Math.max(0, lines.length - 24);
  const tail = lines.slice(floor);
  let end = lines.length - 1;
  while (end >= floor && !lines[end].trim()) end--;
  // A status footer must still terminate the tail; later task output means
  // these familiar-looking lines belong to the transcript.
  if (end < floor || !(codexStatusFooter(lines[end]) || codexLiveModelBranchFooter(lines[end]))) return '';
  // An entered composer draft wins over stale-looking nearby status rows.
  if (tail.some(line => /^(?:[›❯]\s+)\S/.test(line.trim()) && !codexLiveComposer(line, profile))) return '';
  const hasFooter = tail.some(line => codexStatusFooter(line) || codexLiveModelBranchFooter(line));
  const hasBranch = tail.some(codexBranchStatus);
  const hasTimerOrDivider = tail.some(line => workedForLine(line) || divider(line));
  const anchors = [];
  for (let index = floor; index < lines.length; index++) {
    const line = lines[index];
    if (codexComposerWithBranch(line, profile)
      || (codexComposerPlaceholder(line, profile) && (hasFooter || hasBranch || hasTimerOrDivider))
      || (codexBranchStatus(line) && hasFooter)) anchors.push(index);
  }
  if (!anchors.length) return '';
  let model = '';
  for (let index = floor; index < lines.length; index++) {
    const line = lines[index];
    const explicit = codexComposerPlaceholder(line, profile) || codexComposerWithBranch(line, profile) || codexBranchStatus(line);
    const nearby = anchors.some(anchor => Math.abs(anchor - index) <= 6);
    const supporting = codexLiveComposer(line, profile) || workedForLine(line) || divider(line) || codexShortcut(line) || codexStatusFooter(line) || codexLiveModelBranchFooter(line);
    if (!explicit && !(nearby && supporting)) continue;
    const observed = modelName(line); if (observed) model = observed;
    hidden.add(index);
  }
  return model;
}

function codexShortcut(line) {
  return /^(?:esc to interrupt|ctrl\+c to interrupt|press esc to|\/ for commands|\? for (?:shortcuts|help)|thinking\.\.\.)$/i.test(line.trim());
}

// Codex can render the active model, cwd/title, and branch on one line
// without a context percentage. This is intentionally accepted only by the
// tail fallback when an exact live composer anchor is also present.
function codexLiveModelBranchFooter(line) {
  return /^gpt-[\w.-]+\s+(?:low|medium|high|xhigh|max|ultra)\s*·\s*\S(?:.*\S)?\s*·\s*main\s*\[\s*default\s*\]$/i.test(line.trim());
}

function divider(line) { return /^[\s─━—-]{12,}$/.test(line); }
function modelName(line) { const value=line.trim(); const named=value.match(/^model:\s*((?:gpt|claude)[-\w. ]+)$/i) || value.match(/^((?:gpt|claude)[-\w.]+)(?:\s+\w+)?\s*·\s*\d+%\s+(?:left|context left)/i) || value.match(/^((?:gpt|claude)[-\w.]+)\s+(?:low|medium|high|xhigh|max|ultra)\b/i); if(named)return named[1].trim(); return /^(?:gpt|claude)[-\w.]+$/i.test(value) ? value : ''; }
function liveFooterMarker(line) { const value=line.trim(); return /^(?:esc to interrupt|ctrl\+c to interrupt|press esc to|\/ for commands|\? for shortcuts|context (?:left|remaining)|tokens? (?:left|remaining)|thinking\.\.\.|(?:gpt|claude)[-\w.]+|model:\s*(?:gpt|claude)[-\w. ]+|\d+%\s+(?:context|tokens?)\s+(?:left|remaining)|[›❯]\s*|(?:gpt|claude)[-\w.]+(?:\s+\w+)?\s*·\s*\d+%\s+(?:left|context left)(?:\s*·.*)?)$/i.test(value); }
