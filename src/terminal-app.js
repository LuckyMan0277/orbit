import './terminal-app.css';
import { call, notify, on, isDesktop } from './bridge.js';
import { TerminalPane } from './terminal.js';
import { icon } from './ui.js';
import { getDropPlacement } from './terminal-drop-placement.js';
import { groupIds, tabIds, findGroup, addTab, activateTab, dockTab, removeTab, reorderTab, splitGroup, splitCorner, updateTabSplitRatio } from './terminal-tab-tree.js';

const app = document.querySelector('#app');
app.innerHTML = `
  <main class="terminal-app">
    <header class="terminal-titlebar">
      <details class="terminal-menu" id="terminal-menu">
        <summary class="terminal-brand" aria-label="Orbit menu"><img src="./orbit.svg" alt=""><span>Orbit</span>${icon('chevronDown').innerHTML}</summary>
        <nav class="terminal-menu-panel" aria-label="터미널 메뉴">
          <button type="button" data-action="new-window">새 창<kbd>Ctrl Shift N</kbd></button>
          <div class="menu-divider"></div>
          <button type="button" data-action="new-tab">새 터미널 탭<kbd>Ctrl Shift T</kbd></button>
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
const groupHeaders = new Map();
const groupDropZones = new Map();
const splitters = new Map();
const settings = { fontSize: 14, lowPower: false, theme: 'dark' };
let layoutRoot = null;
let activePaneId = '';
let folder = '';
let initialized = false;
let emptyState = null;
let terminalNumber = 0;

const setStatus = text => { status.textContent = text; };
const activePane = () => panes.get(activePaneId) || null;
function groupForTab(root, tabId) {
  if (!root) return null;
  if (root.type === 'group') return root.tabs.includes(tabId) ? root : null;
  return groupForTab(root.first, tabId) || groupForTab(root.second, tabId);
}
const closeMenu = () => { menu.open = false; };
const focusActivePane = () => activePane()?.pane?.term.focus();
const newTerminalName = profile => `${profile === 'cmd' ? '명령 프롬프트' : 'PowerShell'} ${++terminalNumber}`;

document.querySelector('#window-minimize').onclick = () => notify('windowMinimize');
document.querySelector('#window-maximize').onclick = () => notify('windowMaximize');
document.querySelector('#window-close').onclick = () => notify('windowClose');
document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
  closeMenu();
  const actions = {
    'new-window': newWindow,
    'new-tab': () => createTab(),
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
function clearDragState() {
  terminalHost.classList.remove('dragging-tabs');
  terminalHost.querySelectorAll('.terminal-tab.dragging').forEach(tab => tab.classList.remove('dragging'));
  terminalHost.querySelectorAll('.terminal-tab.drop-before,.terminal-tab.drop-after').forEach(tab => tab.classList.remove('drop-before','drop-after'));
  terminalHost.querySelectorAll('.group-drop-zone.visible').forEach(zone => { zone.classList.remove('visible'); delete zone.dataset.placement; });
}
document.addEventListener('dragend', clearDragState, true);
window.addEventListener('blur', clearDragState);
document.addEventListener('visibilitychange', () => { if (document.hidden) clearDragState(); });

function browserSample() {
  terminalHost.innerHTML = '<div class="preview-terminal"><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> Get-Location</div><pre>Path\n----\nC:\\Workspace</pre><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> <span class="preview-cursor"></span></div><p>Browser preview · sample terminal output. Shell commands run in the Windows desktop app.</p></div>';
  setStatus('미리보기');
  document.querySelector('.terminal-menu').hidden = true;
}

function renderLayout() {
  if (!layoutRoot) {
    for (const slot of paneSlots.values()) slot.remove();
    for (const header of groupHeaders.values()) header.remove();
    for (const zone of groupDropZones.values()) zone.remove();
    for (const splitter of splitters.values()) splitter.remove();
    paneSlots.clear(); groupHeaders.clear(); groupDropZones.clear(); splitters.clear();
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
  const activeIds = new Set(tabIds(layoutRoot));
  const activeGroups = new Set(leaves.map(leaf => leaf.node.id));
  const activeSplits = new Set(splitNodes.map(item => item.node.id));
  for (const [id, slot] of paneSlots) if (!activeIds.has(id)) { slot.remove(); paneSlots.delete(id); }
  for (const [id, header] of groupHeaders) if (!activeGroups.has(id)) { header.remove(); groupHeaders.delete(id); }
  for (const [id, zone] of groupDropZones) if (!activeGroups.has(id)) { zone.remove(); groupDropZones.delete(id); }
  for (const [id, splitter] of splitters) if (!activeSplits.has(id)) { splitter.remove(); splitters.delete(id); }
  for (const { node: group, rect } of leaves) {
    const zone = getGroupDropZone(group.id);
    zone.style.left = `${rect.x * 100}%`; zone.style.top = `${rect.y * 100}%`;
    zone.style.width = `${rect.width * 100}%`; zone.style.height = `${rect.height * 100}%`;
    const header = getGroupHeader(group.id);
    const previousScrollLeft = Number(header.querySelector('.terminal-tab-strip')?.scrollLeft || 0);
    header.style.left = `${rect.x * 100}%`; header.style.top = `${rect.y * 100}%`;
    header.style.width = `${rect.width * 100}%`;
    header.setAttribute('role', 'group'); header.setAttribute('aria-label', '터미널 그룹');
    header.classList.toggle('focused', group.id === groupForTab(layoutRoot, activePaneId)?.id);
    header.replaceChildren();
    header.dataset.groupId = group.id;
    const tabStrip = document.createElement('div');
    tabStrip.className = 'terminal-tab-strip'; tabStrip.setAttribute('role', 'tablist'); tabStrip.setAttribute('aria-label', '터미널 탭');
    tabStrip.addEventListener('wheel', event => {
      if (tabStrip.scrollWidth <= tabStrip.clientWidth || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
      event.preventDefault(); tabStrip.scrollLeft += event.deltaY;
    }, { passive: false });
    for (const id of group.tabs) {
      const record = panes.get(id);
      const tab = document.createElement('div');
      tab.className = 'terminal-tab'; tab.draggable = true; tab.tabIndex = id === group.activeTabId ? 0 : -1; tab.setAttribute('role','tab');
      tab.setAttribute('aria-selected', String(id === group.activeTabId));
      tab.classList.toggle('active', id === group.activeTabId);
      tab.dataset.tabId = id; tab.dataset.groupId = group.id; tab.title = record?.name || (record?.profile === 'cmd' ? '명령 프롬프트' : 'PowerShell');
      const label = document.createElement('span'); label.textContent = record?.name || 'PowerShell';
      const close = document.createElement('button'); close.type = 'button'; close.className = 'tab-close'; close.setAttribute('aria-label', `${label.textContent} 탭 닫기`); close.textContent = '×';
      close.onclick = event => { event.stopPropagation(); closeTab(id); };
      tab.append(label, close);
      tab.onclick = () => activateSession(group.id, id);
      tab.onkeydown = event => { if (event.target !== tab) return; if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); activateSession(group.id, id); } };
      tab.ondragstart = event => { event.dataTransfer.setData('application/x-orbit-terminal-tab', JSON.stringify({ groupId: group.id, tabId: id })); event.dataTransfer.effectAllowed = 'move'; tab.classList.add('dragging'); terminalHost.classList.add('dragging-tabs'); };
      tab.ondragover = event => {
        event.preventDefault(); event.stopPropagation(); event.dataTransfer.dropEffect = 'move';
        const bounds = tab.getBoundingClientRect();
        tab.classList.toggle('drop-before', event.clientX < bounds.left + bounds.width / 2);
        tab.classList.toggle('drop-after', event.clientX >= bounds.left + bounds.width / 2);
      };
      tab.ondragleave = event => { if (!tab.contains(event.relatedTarget)) tab.classList.remove('drop-before','drop-after'); };
      tab.ondrop = event => { event.preventDefault(); event.stopPropagation(); tab.classList.remove('drop-before','drop-after'); const data = readDrag(event); if (!data) return; const siblings = [...header.querySelectorAll('.terminal-tab')]; const index = siblings.indexOf(tab) + (event.clientX >= tab.getBoundingClientRect().left + tab.getBoundingClientRect().width / 2 ? 1 : 0); dockDragged(data, group.id, 'center', index); };
      tabStrip.append(tab);
    }
    header.append(tabStrip);
    tabStrip.scrollLeft = previousScrollLeft;
    if (group.tabs.length) {
      const add = document.createElement('button'); add.type = 'button'; add.className = 'group-add-tab'; add.title = '새 터미널 탭'; add.setAttribute('aria-label', '새 터미널 탭'); add.textContent = '+'; add.onclick = () => createTab(group.id); header.append(add);
    }
    header.ondragover = event => {
      if (!event.dataTransfer.types.includes('application/x-orbit-terminal-tab')) return;
      event.preventDefault(); event.dataTransfer.dropEffect = 'move';
      const targetZone = groupDropZones.get(group.id);
      targetZone.dataset.placement = 'center'; targetZone.classList.add('visible');
    };
    header.ondragleave = event => { if (!header.contains(event.relatedTarget)) { const targetZone = groupDropZones.get(group.id); targetZone.classList.remove('visible'); delete targetZone.dataset.placement; } };
    header.ondrop = event => { event.preventDefault(); event.stopPropagation(); const data = readDrag(event); if (data) dockDragged(data, group.id, 'center', group.tabs.length); };
    if (!group.tabs.length) {
      const empty = document.createElement('button'); empty.type = 'button'; empty.className = 'empty-group-add'; empty.textContent = '+'; empty.title = '이 영역에 터미널 추가'; empty.onclick = () => createTab(group.id); header.append(empty);
    }
    revealTab(tabStrip, tabStrip.querySelector(`[data-tab-id="${group.activeTabId}"]`));
    for (const id of group.tabs) {
      const record = panes.get(id); if (!record) continue;
      const slot = getPaneSlot(id);
      slot.style.left = `${rect.x * 100}%`; slot.style.top = `calc(${rect.y * 100}% + 30px)`;
      slot.style.width = `${rect.width * 100}%`; slot.style.height = `calc(${rect.height * 100}% - 30px)`;
      slot.classList.toggle('active', id === activePaneId);
      slot.style.visibility = id === group.activeTabId ? 'visible' : 'hidden';
      slot.style.zIndex = id === group.activeTabId ? '2' : '0';
      slot.querySelector('.pane-feedback')?.remove();
      if (record.error || record.starting && !record.pane) {
        const feedback = document.createElement('div'); feedback.className = 'pane-feedback';
        const message = document.createElement('p'); message.textContent = record.error || '터미널 시작 중…'; feedback.append(message);
        if (record.error) { const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry'; retry.onclick = () => launchPane(id, record.profile); feedback.append(retry); }
        slot.append(feedback);
      }
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
  if (node.type === 'group') { leaves.push({ node, rect }); return; }
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
  slot.addEventListener('pointerdown', () => activateSession(groupForTab(layoutRoot, id)?.id, id));
  paneSlots.set(id, slot); terminalHost.append(slot);
  return slot;
}

function getGroupHeader(id) {
  let header = groupHeaders.get(id);
  if (header) return header;
  header = document.createElement('div'); header.className = 'terminal-group-header'; header.dataset.groupId = id;
  groupHeaders.set(id, header); terminalHost.append(header);
  return header;
}

function getGroupDropZone(id) {
  let zone = groupDropZones.get(id);
  if (zone) return zone;
  zone = document.createElement('div'); zone.className = 'group-drop-zone'; zone.dataset.groupId = id;
  zone.ondragover = event => {
    if (!event.dataTransfer.types.includes('application/x-orbit-terminal-tab')) return;
    event.preventDefault(); event.dataTransfer.dropEffect = 'move';
    const bounds = zone.getBoundingClientRect();
    const placement = getDropPlacement(bounds, { x: event.clientX, y: event.clientY });
    zone.dataset.placement = placement;
    zone.classList.add('visible');
  };
  zone.ondragleave = event => { if (!zone.contains(event.relatedTarget)) { zone.classList.remove('visible'); delete zone.dataset.placement; } };
  zone.ondrop = event => {
    event.preventDefault();
    const bounds = zone.getBoundingClientRect();
    const data = readDrag(event), placement = getDropPlacement(bounds, { x: event.clientX, y: event.clientY });
    zone.classList.remove('visible'); delete zone.dataset.placement;
    terminalHost.classList.remove('dragging-tabs');
    if (data) dockDragged(data, id, placement);
  };
  groupDropZones.set(id, zone); terminalHost.append(zone);
  return zone;
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
      layoutRoot = updateTabSplitRatio(layoutRoot, id, start + (position - origin) / axis);
      renderLayout();
    };
    splitter.onpointerup = () => { splitter.onpointermove = null; splitter.onpointerup = null; };
  };
  splitter.onkeydown = event => {
    const node = findSplit(layoutRoot, id);
    if (!node || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const direction = node.direction === 'row' ? (event.key === 'ArrowLeft' ? -1 : 1) : (event.key === 'ArrowUp' ? -1 : 1);
    layoutRoot = updateTabSplitRatio(layoutRoot, id, node.ratio + direction * 0.04);
    renderLayout();
  };
  splitter.setAttribute('aria-label', '분할 크기 조절');
  splitters.set(id, splitter); terminalHost.append(splitter);
  return splitter;
}

function findSplit(node, id) {
  if (!node || node.type === 'group') return null;
  if (node.id === id) return node;
  return findSplit(node.first, id) || findSplit(node.second, id);
}

function findSplitRect(node, id, rect) {
  if (!node || node.type === 'group') return null;
  if (node.id === id) return rect;
  if (node.direction === 'row') {
    const firstWidth = rect.width * node.ratio;
    return findSplitRect(node.first, id, { ...rect, width: firstWidth }) || findSplitRect(node.second, id, { ...rect, x: rect.x + firstWidth, width: rect.width - firstWidth });
  }
  const firstHeight = rect.height * node.ratio;
  return findSplitRect(node.first, id, { ...rect, height: firstHeight }) || findSplitRect(node.second, id, { ...rect, y: rect.y + firstHeight, height: rect.height - firstHeight });
}

function activateSession(groupId, id) {
  if (!panes.has(id) || !groupId) return;
  layoutRoot = activateTab(layoutRoot, groupId, id);
  activePaneId = id;
  const visibleTabs = new Set(groupIds(layoutRoot).map(groupId => findGroup(layoutRoot, groupId)?.activeTabId).filter(Boolean));
  document.querySelectorAll('.pane-slot').forEach(slot => {
    const selected = slot.dataset.paneId === id;
    slot.classList.toggle('active', selected);
    const visible = visibleTabs.has(slot.dataset.paneId);
    slot.style.visibility = visible ? 'visible' : 'hidden';
    slot.style.zIndex = selected ? '2' : visible ? '1' : '0';
  });
  const focusedGroupId = groupForTab(layoutRoot, id)?.id;
  document.querySelectorAll('.terminal-tab').forEach(tab => {
    const selected = findGroup(layoutRoot, tab.dataset.groupId)?.activeTabId === tab.dataset.tabId;
    tab.classList.toggle('active', selected); tab.tabIndex = selected ? 0 : -1; tab.setAttribute('aria-selected', String(selected));
  });
  document.querySelectorAll('.terminal-group-header').forEach(header => header.classList.toggle('focused', header.dataset.groupId === focusedGroupId));
  const activeTabElement = document.querySelector(`.terminal-tab[data-tab-id="${id}"]`);
  revealTab(activeTabElement?.closest('.terminal-tab-strip'), activeTabElement);
  updateActiveStatus();
  focusActivePane();
}

function revealTab(strip, tab) {
  if (!strip || !tab) return;
  const stripBounds = strip.getBoundingClientRect(), tabBounds = tab.getBoundingClientRect();
  if (tabBounds.left < stripBounds.left) strip.scrollLeft -= stripBounds.left - tabBounds.left;
  else if (tabBounds.right > stripBounds.right) strip.scrollLeft += tabBounds.right - stripBounds.right;
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
  const groupId = crypto.randomUUID();
  layoutRoot = { type: 'group', id: groupId, tabs: [id], activeTabId: id };
  activePaneId = id;
  panes.set(id, { profile, name: newTerminalName(profile), cwd: folder, starting: true, pane: null, error: '' });
  renderLayout();
  await launchPane(id, profile);
}

async function createTab(groupId) {
  if (!isDesktop) return;
  if (panes.size >= 8) return showLimitError();
  groupId ||= groupForTab(layoutRoot, activePaneId)?.id || groupIds(layoutRoot).find(id => !findGroup(layoutRoot, id)?.tabs.length);
  if (!layoutRoot || !groupId) return createFirstPane('powershell');
  const id = crypto.randomUUID();
  const previous = activePane();
  const profile = previous?.profile || 'powershell';
  layoutRoot = addTab(layoutRoot, groupId, id);
  activePaneId = id;
  panes.set(id, { profile, name: newTerminalName(profile), cwd: previous?.cwd || folder, starting: true, pane: null, error: '' });
  renderLayout();
  const slot = getPaneSlot(id); slot.style.visibility = 'visible'; slot.style.zIndex = '2';
  await launchPane(id, profile);
}

async function splitActive(direction) {
  if (!layoutRoot) return createFirstPane('powershell');
  if (panes.size >= 8) return showLimitError();
  const id = crypto.randomUUID();
  const previous = activePane();
  const profile = previous?.profile || 'powershell';
  const group = groupForTab(layoutRoot, activePaneId);
  const next = splitGroup(layoutRoot, group?.id, crypto.randomUUID(), id, direction, crypto.randomUUID());
  if (next === layoutRoot) return;
  layoutRoot = next;
  activePaneId = id;
  panes.set(id, { profile, name: newTerminalName(profile), cwd: folder, starting: true, pane: null, error: '' });
  renderLayout();
  await launchPane(id, profile);
}

function showLimitError() {
  errorBox.textContent = '현재 창에는 최대 8개의 터미널 탭을 둘 수 있습니다. 먼저 다른 탭을 닫아 주세요.';
  errorBox.hidden = false;
}

async function launchPane(paneId, profile) {
  const previous = panes.get(paneId);
  if (!previous || !isDesktop) return;
  if (previous.pane?.started && !previous.pane.exited) notify('closeTerminal', { session: previous.pane.id });
  previous.pane?.dispose();
  const record = { profile, name: profile === previous.profile ? previous.name : newTerminalName(profile), cwd: previous.cwd || folder, starting: true, pane: null, error: '' };
  panes.set(paneId, record);
  const sessionId = `terminal-${crypto.randomUUID()}`;
  const terminal = new TerminalPane({
    id: sessionId, profile, cwd: record.cwd, name: record.name,
    settings, openLink: url => call('external', { path: url, cwd: record.cwd }),
    workspaceShortcuts: false, layoutMode: 'managed', scrollback: 5000, lineHeight: 1.2,
    onFocus: () => activateSession(groupForTab(layoutRoot, paneId)?.id, paneId),
    onExit: () => {
      if (panes.get(paneId) !== record) return;
      record.starting = false;
      updateActiveStatus();
    },
  });
  record.pane = terminal;
  getPaneSlot(paneId).append(terminal.element);
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
  closeTab(activePaneId);
}

function closeTab(id) {
  if (!id || !panes.has(id)) return;
  const wasActive = activePaneId === id;
  const oldGroup = groupForTab(layoutRoot, id);
  const record = panes.get(id);
  if (record?.pane?.started && !record.pane.exited) notify('closeTerminal', { session: record.pane.id });
  record?.pane?.dispose();
  panes.delete(id);
  layoutRoot = removeTab(layoutRoot, id);
  if (wasActive) {
    const surviving = oldGroup && findGroup(layoutRoot, oldGroup.id);
    activePaneId = surviving?.activeTabId || '';
    if (!activePaneId) {
      for (const groupId of groupIds(layoutRoot)) {
        const candidate = findGroup(layoutRoot, groupId)?.activeTabId;
        if (candidate) { activePaneId = candidate; break; }
      }
    }
  }
  errorBox.hidden = true;
  renderLayout();
  focusActivePane();
}

function readDrag(event) {
  try { return JSON.parse(event.dataTransfer.getData('application/x-orbit-terminal-tab')); } catch { return null; }
}

function dockDragged(data, targetGroupId, placement, index) {
  if (!data || !findGroup(layoutRoot, data.groupId) || !findGroup(layoutRoot, targetGroupId)) return;
  terminalHost.classList.remove('dragging-tabs');
  if (placement === 'center' && Number.isInteger(index)) {
    if (data.groupId === targetGroupId) {
      const source = findGroup(layoutRoot, data.groupId), from = source.tabs.indexOf(data.tabId);
      layoutRoot = reorderTab(layoutRoot, data.groupId, data.tabId, index - (from < index ? 1 : 0));
    } else {
      layoutRoot = dockTab(layoutRoot, data.groupId, data.tabId, targetGroupId, placement, { groupId: crypto.randomUUID(), splitId: crypto.randomUUID() }, index);
    }
  } else if (placement.includes('-')) {
    layoutRoot = splitCorner(layoutRoot, data.groupId, targetGroupId, data.tabId, placement, {
      emptyGroupId: crypto.randomUUID(), innerSplitId: crypto.randomUUID(), tabGroupId: crypto.randomUUID(), outerSplitId: crypto.randomUUID(),
    });
  } else layoutRoot = dockTab(layoutRoot, data.groupId, data.tabId, targetGroupId, placement, { groupId: crypto.randomUUID(), splitId: crypto.randomUUID() });
  const group = groupForTab(layoutRoot, data.tabId);
  if (group) { layoutRoot = activateTab(layoutRoot, group.id, data.tabId); activePaneId = data.tabId; }
  renderLayout(); focusActivePane();
}

function newWindow() {
  notify('newWindow', { cwd: activePane()?.cwd || folder });
  focusActivePane();
}

function handleShortcut(event) {
  if (event.key === 'Escape' && terminalHost.classList.contains('dragging-tabs')) {
    event.preventDefault(); event.stopImmediatePropagation(); clearDragState(); return;
  }
  if (event.key === 'Escape' && menu.open) {
    event.preventDefault();
    event.stopImmediatePropagation();
    closeMenu();
    focusActivePane();
    return;
  }
  if (event.ctrlKey && !event.altKey && event.key === 'Tab') {
    event.preventDefault(); event.stopImmediatePropagation();
    const group = groupForTab(layoutRoot, activePaneId);
    if (group?.tabs.length > 1) {
      const current = group.tabs.indexOf(activePaneId), delta = event.shiftKey ? -1 : 1;
      const next = group.tabs[(current + delta + group.tabs.length) % group.tabs.length];
      activateSession(group.id, next);
    }
    return;
  }
  if (!event.ctrlKey || !event.shiftKey || event.altKey) return;
  const action = ({ KeyN: newWindow, KeyT: () => createTab(), KeyD: () => splitActive('row'), KeyE: () => splitActive('column'), KeyR: () => restartActive(), KeyW: closeActivePane })[event.code];
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

const treeSnapshot = node => !node ? null : node.type === 'group'
  ? { type: 'group', id: node.id, tabs: [...node.tabs], activeTabId: node.activeTabId }
  : { type: 'split', id: node.id, direction: node.direction, ratio: node.ratio, first: treeSnapshot(node.first), second: treeSnapshot(node.second) };
window.orbitDiagnostics = () => ({ ready: initialized && !!activePane()?.pane?.started, windowControls: 3, activePaneId, profile: activePane()?.profile || '', cwd: activePane()?.cwd || folder, cols: activePane()?.pane?.term.cols || 0, rows: activePane()?.pane?.term.rows || 0, sessions: [...panes].map(([id, record]) => paneDiagnostics(id, record)) });
window.orbitUiTest = {
  startCmd: () => restartActive('cmd'),
  startPowerShell: () => restartActive('powershell'),
  send: async data => { const pane = activePane()?.pane; if (!pane?.started) throw new Error('active terminal is not running'); notify('write', { session: pane.id, data }); },
  split: direction => splitActive(direction === 'column' || direction === 'below' ? 'column' : 'row'),
  newTab: () => createTab(),
  activateTab: id => { const group = groupForTab(layoutRoot, id); if (group) activateSession(group.id, id); },
  closeTab,
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
