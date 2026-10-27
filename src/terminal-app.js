import './terminal-app.css';
import { call, notify, on, isDesktop } from './bridge.js';
import { TerminalPane } from './terminal.js';
import { icon } from './ui.js';
import { paneIds, removePane, splitPane, updateSplitRatio } from './terminal-split-tree.js';

const app = document.querySelector('#app');
app.innerHTML = `
  <main class="terminal-app">
    <header class="terminal-titlebar">
      <details class="terminal-menu" id="terminal-menu">
        <summary class="terminal-brand" aria-label="Orbit menu"><img src="./orbit.svg" alt=""><span>Orbit</span>${icon('chevronDown').innerHTML}</summary>
        <nav class="terminal-menu-panel" aria-label="터미널 메뉴">
          <button type="button" data-action="new-window">새 창<kbd>Ctrl Shift N</kbd></button>
          <div class="menu-divider"></div>
          <button type="button" data-action="split-right">오른쪽으로 분할<kbd>Ctrl Shift D</kbd></button>
          <button type="button" data-action="split-below">아래로 분할<kbd>Ctrl Shift E</kbd></button>
          <div class="menu-divider"></div>
          <button type="button" data-action="restart">현재 터미널 다시 시작<kbd>Ctrl Shift R</kbd></button>
          <button type="button" data-action="restart-powershell">PowerShell로 다시 시작</button>
          <button type="button" data-action="restart-cmd">명령 프롬프트로 다시 시작</button>
          <div class="menu-divider"></div>
          <button type="button" data-action="close-active">현재 터미널 닫기<kbd>Ctrl Shift W</kbd></button>
        </nav>
      </details>
      <div class="terminal-title-actions"><span class="terminal-status" id="terminal-status" role="status">시작 중</span><div class="terminal-window-controls" aria-label="창 제어"><button id="window-minimize" title="최소화" aria-label="최소화">${icon('minimize').innerHTML}</button><button id="window-maximize" title="최대화 또는 복원" aria-label="최대화 또는 복원">${icon('maximize').innerHTML}</button><button id="window-close" class="close" title="닫기" aria-label="닫기">${icon('close').innerHTML}</button></div></div>
    </header>
    <section id="terminal-host" class="terminal-host" aria-label="터미널"></section>
    <div id="terminal-error" class="terminal-error" role="alert" hidden></div>
  </main>`;

const terminalHost = document.querySelector('#terminal-host');
const status = document.querySelector('#terminal-status');
const errorBox = document.querySelector('#terminal-error');
const menu = document.querySelector('#terminal-menu');
const panes = new Map();
const paneSlots = new Map();
const splitters = new Map();
const settings = { fontSize: 14, lowPower: false, theme: 'dark' };
let layoutRoot = null;
let activePaneId = '';
let folder = '';
let initialized = false;
let emptyState = null;

const setStatus = text => { status.textContent = text; };
const activePane = () => panes.get(activePaneId) || null;
const closeMenu = () => { menu.open = false; };
const focusActivePane = () => activePane()?.pane?.term.focus();

document.querySelector('#window-minimize').onclick = () => notify('windowMinimize');
document.querySelector('#window-maximize').onclick = () => notify('windowMaximize');
document.querySelector('#window-close').onclick = () => notify('windowClose');
document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
  closeMenu();
  const actions = {
    'new-window': newWindow,
    'split-right': () => splitActive('row'),
    'split-below': () => splitActive('column'),
    restart: () => restartActive(),
    'restart-powershell': () => restartActive('powershell'),
    'restart-cmd': () => restartActive('cmd'),
    'close-active': () => closeActivePane(),
  };
  actions[button.dataset.action]?.();
}));
document.addEventListener('pointerdown', event => { if (menu.open && !menu.contains(event.target)) closeMenu(); });

function browserSample() {
  terminalHost.innerHTML = '<div class="preview-terminal"><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> Get-Location</div><pre>Path\n----\nC:\\Workspace</pre><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> <span class="preview-cursor"></span></div><p>Browser preview · sample terminal output. Shell commands run in the Windows desktop app.</p></div>';
  setStatus('미리보기');
  document.querySelector('.terminal-menu').hidden = true;
}

function renderLayout() {
  if (!layoutRoot) {
    for (const slot of paneSlots.values()) slot.remove();
    for (const splitter of splitters.values()) splitter.remove();
    paneSlots.clear(); splitters.clear();
    if (!emptyState) {
      emptyState = document.createElement('div');
      emptyState.className = 'terminal-empty';
      emptyState.innerHTML = '<p>열린 터미널이 없습니다.</p><div><button type="button" data-empty-shell="powershell">PowerShell 열기</button><button type="button" data-empty-shell="cmd">명령 프롬프트 열기</button></div>';
      emptyState.querySelectorAll('[data-empty-shell]').forEach(button => button.addEventListener('click', () => createFirstPane(button.dataset.emptyShell)));
    }
    terminalHost.append(emptyState);
    setStatus('터미널 없음');
    return;
  }
  emptyState?.remove();
  const leaves = [], splitNodes = [];
  collectLayout(layoutRoot, { x: 0, y: 0, width: 1, height: 1 }, leaves, splitNodes);
  const activeIds = new Set(leaves.map(leaf => leaf.node.id));
  const activeSplits = new Set(splitNodes.map(item => item.node.id));
  for (const [id, slot] of paneSlots) if (!activeIds.has(id)) { slot.remove(); paneSlots.delete(id); }
  for (const [id, splitter] of splitters) if (!activeSplits.has(id)) { splitter.remove(); splitters.delete(id); }
  for (const { node, rect } of leaves) {
    const slot = getPaneSlot(node.id);
    slot.style.left = `${rect.x * 100}%`; slot.style.top = `${rect.y * 100}%`;
    slot.style.width = `${rect.width * 100}%`; slot.style.height = `${rect.height * 100}%`;
    slot.classList.toggle('active', node.id === activePaneId);
    const record = panes.get(node.id);
    if (record?.pane && record.pane.element.parentElement !== slot) slot.append(record.pane.element);
    slot.querySelector('.pane-feedback')?.remove();
    if (record?.error || record?.starting && !record.pane) {
      const feedback = document.createElement('div');
      feedback.className = 'pane-feedback';
      const message = document.createElement('p'); message.textContent = record.error || '터미널 시작 중…';
      feedback.append(message);
      if (record.error) { const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry'; retry.onclick = () => launchPane(node.id, record.profile); feedback.append(retry); }
      slot.append(feedback);
    }
  }
  for (const item of splitNodes) {
    const splitter = getSplitter(item.node.id);
    const { node, rect } = item;
    splitter.classList.toggle('row', node.direction === 'row');
    splitter.classList.toggle('column', node.direction === 'column');
    splitter.setAttribute('aria-orientation', node.direction === 'row' ? 'vertical' : 'horizontal');
    if (node.direction === 'row') {
      splitter.style.left = `${(rect.x + rect.width * node.ratio) * 100}%`;
      splitter.style.top = `${rect.y * 100}%`; splitter.style.height = `${rect.height * 100}%`; splitter.style.width = '';
    } else {
      splitter.style.left = `${rect.x * 100}%`;
      splitter.style.top = `${(rect.y + rect.height * node.ratio) * 100}%`; splitter.style.width = `${rect.width * 100}%`; splitter.style.height = '';
    }
  }
  updateActiveStatus();
}

function collectLayout(node, rect, leaves, splits) {
  if (node.type === 'pane') { leaves.push({ node, rect }); return; }
  splits.push({ node, rect });
  if (node.direction === 'row') {
    const firstWidth = rect.width * node.ratio;
    collectLayout(node.first, { ...rect, width: firstWidth }, leaves, splits);
    collectLayout(node.second, { ...rect, x: rect.x + firstWidth, width: rect.width - firstWidth }, leaves, splits);
  } else {
    const firstHeight = rect.height * node.ratio;
    collectLayout(node.first, { ...rect, height: firstHeight }, leaves, splits);
    collectLayout(node.second, { ...rect, y: rect.y + firstHeight, height: rect.height - firstHeight }, leaves, splits);
  }
}

function getPaneSlot(id) {
  let slot = paneSlots.get(id);
  if (slot) return slot;
  slot = document.createElement('div'); slot.className = 'pane-slot'; slot.dataset.paneId = id;
  slot.addEventListener('pointerdown', () => activatePane(id));
  paneSlots.set(id, slot); terminalHost.append(slot);
  return slot;
}

function getSplitter(id) {
  let splitter = splitters.get(id);
  if (splitter) return splitter;
  splitter = document.createElement('div');
  splitter.className = 'terminal-splitter';
  splitter.dataset.splitId = id;
  splitter.tabIndex = 0;
  splitter.setAttribute('role', 'separator');
  splitter.onpointerdown = event => {
    event.preventDefault();
    splitter.setPointerCapture(event.pointerId);
    const node = findSplit(layoutRoot, id);
    const rect = findSplitRect(layoutRoot, id, { x: 0, y: 0, width: 1, height: 1 });
    if (!node || !rect) return;
    const direction = node.direction, start = node.ratio;
    const origin = direction === 'row' ? event.clientX : event.clientY;
    const host = terminalHost.getBoundingClientRect();
    const axis = direction === 'row' ? rect.width * host.width : rect.height * host.height;
    splitter.onpointermove = move => {
      const position = direction === 'row' ? move.clientX : move.clientY;
      layoutRoot = updateSplitRatio(layoutRoot, id, start + (position - origin) / axis);
      renderLayout();
    };
    splitter.onpointerup = () => { splitter.onpointermove = null; splitter.onpointerup = null; };
  };
  splitter.onkeydown = event => {
    const node = findSplit(layoutRoot, id);
    if (!node || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const direction = node.direction === 'row' ? (event.key === 'ArrowLeft' ? -1 : 1) : (event.key === 'ArrowUp' ? -1 : 1);
    layoutRoot = updateSplitRatio(layoutRoot, id, node.ratio + direction * 0.04);
    renderLayout();
  };
  splitter.setAttribute('aria-label', '분할 크기 조절');
  splitters.set(id, splitter); terminalHost.append(splitter);
  return splitter;
}

function findSplit(node, id) {
  if (!node || node.type === 'pane') return null;
  if (node.id === id) return node;
  return findSplit(node.first, id) || findSplit(node.second, id);
}

function findSplitRect(node, id, rect) {
  if (!node || node.type === 'pane') return null;
  if (node.id === id) return rect;
  if (node.direction === 'row') {
    const firstWidth = rect.width * node.ratio;
    return findSplitRect(node.first, id, { ...rect, width: firstWidth }) || findSplitRect(node.second, id, { ...rect, x: rect.x + firstWidth, width: rect.width - firstWidth });
  }
  const firstHeight = rect.height * node.ratio;
  return findSplitRect(node.first, id, { ...rect, height: firstHeight }) || findSplitRect(node.second, id, { ...rect, y: rect.y + firstHeight, height: rect.height - firstHeight });
}

function activatePane(id) {
  if (!panes.has(id) || activePaneId === id) return;
  activePaneId = id;
  document.querySelectorAll('.pane-slot').forEach(slot => slot.classList.toggle('active', slot.dataset.paneId === id));
  updateActiveStatus();
}

function updateActiveStatus() {
  const record = activePane();
  if (!record) { setStatus('터미널 없음'); return; }
  const profile = record.profile === 'cmd' ? '명령 프롬프트' : 'PowerShell';
  setStatus(record.starting ? `${profile} · 시작 중` : record.error ? `${profile} · 시작 실패` : record.pane?.exited ? `${profile} · 종료` : `${profile} · 실행 중`);
}

async function createFirstPane(profile = 'powershell') {
  if (!isDesktop) return;
  const id = crypto.randomUUID();
  layoutRoot = { type: 'pane', id };
  activePaneId = id;
  panes.set(id, { profile, cwd: folder, starting: true, pane: null, error: '' });
  renderLayout();
  await launchPane(id, profile);
}

async function splitActive(direction) {
  if (!layoutRoot) return createFirstPane('powershell');
  if (panes.size >= 8) {
    errorBox.textContent = '현재 창에는 최대 8개의 터미널을 둘 수 있습니다. 먼저 다른 터미널을 닫아 주세요.';
    errorBox.hidden = false;
    return;
  }
  const id = crypto.randomUUID();
  const previous = activePane();
  const profile = previous?.profile || 'powershell';
  const next = splitPane(layoutRoot, activePaneId, id, direction, crypto.randomUUID());
  if (next === layoutRoot) return;
  layoutRoot = next;
  activePaneId = id;
  panes.set(id, { profile, cwd: folder, starting: true, pane: null, error: '' });
  renderLayout();
  await launchPane(id, profile);
}

async function launchPane(paneId, profile) {
  const previous = panes.get(paneId);
  if (!previous || !isDesktop) return;
  if (previous.pane?.started && !previous.pane.exited) notify('closeTerminal', { session: previous.pane.id });
  previous.pane?.dispose();
  const record = { profile, cwd: previous.cwd || folder, starting: true, pane: null, error: '' };
  panes.set(paneId, record);
  const sessionId = `terminal-${crypto.randomUUID()}`;
  const terminal = new TerminalPane({
    id: sessionId, profile, cwd: record.cwd, name: profile === 'cmd' ? 'Command Prompt' : 'PowerShell',
    settings, openLink: url => call('external', { path: url, cwd: record.cwd }),
    workspaceShortcuts: false, layoutMode: 'managed', scrollback: 5000, lineHeight: 1.2,
    onFocus: () => activatePane(paneId),
    onExit: () => {
      if (panes.get(paneId) !== record) return;
      record.starting = false;
      updateActiveStatus();
    },
  });
  record.pane = terminal;
  renderLayout();
  terminal.term.focus();
  try {
    const started = await terminal.start();
    if (panes.get(paneId) !== record) return;
    record.starting = false;
    if (!started) return;
    record.error = '';
    errorBox.hidden = true;
    updateActiveStatus();
    if (activePaneId === paneId) terminal.focus();
  } catch (error) {
    if (panes.get(paneId) !== record) return;
    terminal.dispose();
    record.pane = null;
    record.starting = false;
    record.error = error.message;
    renderLayout();
  }
}

async function restartActive(profile = activePane()?.profile || 'powershell') {
  if (!activePaneId) return createFirstPane(profile);
  await launchPane(activePaneId, profile);
}

function closeActivePane() {
  if (!activePaneId) return;
  const id = activePaneId;
  const record = panes.get(id);
  if (record?.pane?.started && !record.pane.exited) notify('closeTerminal', { session: record.pane.id });
  record?.pane?.dispose();
  panes.delete(id);
  layoutRoot = removePane(layoutRoot, id);
  activePaneId = paneIds(layoutRoot)[0] || '';
  errorBox.hidden = true;
  renderLayout();
  focusActivePane();
}

function newWindow() {
  notify('newWindow', { cwd: activePane()?.cwd || folder });
  focusActivePane();
}

function handleShortcut(event) {
  if (event.key === 'Escape' && menu.open) {
    event.preventDefault();
    event.stopImmediatePropagation();
    closeMenu();
    focusActivePane();
    return;
  }
  if (!event.ctrlKey || !event.shiftKey || event.altKey) return;
  const action = ({ KeyN: newWindow, KeyD: () => splitActive('row'), KeyE: () => splitActive('column'), KeyR: () => restartActive(), KeyW: closeActivePane })[event.code];
  if (!action) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  closeMenu();
  void action();
}
document.addEventListener('keydown', handleShortcut, true);

async function initialize() {
  if (!isDesktop) { browserSample(); return; }
  try {
    const data = await call('init');
    folder = typeof data.folder === 'string' ? data.folder : '';
    initialized = true;
    document.title = 'Orbit · Terminal';
    await createFirstPane('powershell');
  } catch (error) {
    errorBox.textContent = `Orbit을 초기화하지 못했습니다. ${error.message}`;
    errorBox.hidden = false;
    setStatus('시작 실패');
  }
}

function paneDiagnostics(id, record) {
  const pane = record.pane;
  const buffer = pane?.term.buffer.active;
  return {
    id, sessionId: pane?.id || '', profile: record.profile, pid: pane?.pid, started: !!pane?.started, exited: !!pane?.exited,
    cols: pane?.term.cols || 0, rows: pane?.term.rows || 0,
    buffer: buffer ? { baseY: buffer.baseY, viewportY: buffer.viewportY, length: buffer.length, type: buffer.type } : { baseY: 0, viewportY: 0, length: 0, type: '' },
  };
}

const treeSnapshot = node => !node ? null : node.type === 'pane'
  ? { type: 'pane', id: node.id }
  : { type: 'split', id: node.id, direction: node.direction, ratio: node.ratio, first: treeSnapshot(node.first), second: treeSnapshot(node.second) };
window.orbitDiagnostics = () => ({ ready: initialized && !!activePane()?.pane?.started, windowControls: 3, activePaneId, profile: activePane()?.profile || '', cwd: activePane()?.cwd || folder, cols: activePane()?.pane?.term.cols || 0, rows: activePane()?.pane?.term.rows || 0, sessions: [...panes].map(([id, record]) => paneDiagnostics(id, record)) });
window.orbitUiTest = {
  startCmd: () => restartActive('cmd'),
  startPowerShell: () => restartActive('powershell'),
  send: async data => { const pane = activePane()?.pane; if (!pane?.started) throw new Error('active terminal is not running'); notify('write', { session: pane.id, data }); },
  split: direction => splitActive(direction === 'column' || direction === 'below' ? 'column' : 'row'),
  closeActive: closeActivePane,
  diagnostics: () => {
    const selected = activePane();
    const current = selected?.pane;
    const buffer = current?.term.buffer.active;
    return {
      ready: !!current?.started, started: !!current?.started, exited: !!current?.exited,
      activePaneId, cols: current?.term.cols || 0, rows: current?.term.rows || 0,
      buffer: buffer ? { baseY: buffer.baseY, viewportY: buffer.viewportY, length: buffer.length, type: buffer.type } : { baseY: 0, viewportY: 0, length: 0, type: '' },
      layout: treeSnapshot(layoutRoot), sessions: [...panes].map(([id, record]) => paneDiagnostics(id, record)),
      output: current ? Array.from({ length: buffer.length }, (_, i) => buffer.getLine(i)?.translateToString(true) || '').join('\n') : '',
    };
  },
};
on('error', data => { errorBox.textContent = data.message; errorBox.hidden = false; });
window.addEventListener('beforeunload', () => { for (const record of panes.values()) record.pane?.dispose(); });
renderLayout();
initialize();
