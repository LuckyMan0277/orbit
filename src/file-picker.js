import { call } from './bridge.js';
import { el, icon } from './ui.js';
import { joinPath } from './platform.js';

let openPromise;

const titleFor = mode => ({ folder: '작업 폴더 선택', file: '파일 열기', create: '새 파일 만들기' })[mode] || '파일 선택';
const join = joinPath;

export function openFilePicker(options) {
  if (openPromise) return openPromise;
  openPromise = new Picker(options).open().finally(() => { openPromise = null; });
  return openPromise;
}

class Picker {
  constructor(options) {
    this.options = options;
    this.mode = options.mode;
    this.path = options.cwd;
    this.selected = null;
    this.entries = [];
    this.rows = new Map();
    this.hidden = false;
    this.request = 0;
    this.busy = false;
    this.loaded = false;
    this.history = [this.path];
    this.historyIndex = 0;
    this.previousFocus = document.activeElement;
  }

  async open() {
    this.dialog = document.createElement('dialog');
    this.dialog.id = 'file-picker';
    this.dialog.className = 'file-picker';
    this.dialog.innerHTML = `<header class="file-picker-header"><div><small>${this.mode === 'folder' ? 'WORKSPACE' : 'LOCAL FILES'}</small><h2>${titleFor(this.mode)}</h2></div><button type="button" class="icon-button" data-picker-action="cancel" aria-label="닫기" title="닫기"></button></header>`;
    this.dialog.querySelector('[data-picker-action=cancel]').append(icon('close'));
    this.build();
    this.dialog.querySelector('[data-picker-action=cancel]').addEventListener('click', () => this.finish(null));
    document.body.append(this.dialog);
    this.dialog.addEventListener('cancel', event => { event.preventDefault(); this.finish(null); });
    this.dialog.showModal();
    this.pathInput.focus();
    this.placesPromise = call('pickerPlaces', { cwd: this.options.cwd }).then(value => this.renderPlaces(value)).catch(() => {});
    this.navigate(this.path);
    return new Promise(resolve => { this.resolve = resolve; });
  }

  build() {
    const content = el('div', 'file-picker-content');
    const sidebar = el('aside', 'file-picker-places');
    sidebar.append(el('p', 'file-picker-label', '바로 가기'));
    this.places = el('div', 'file-picker-place-list');
    sidebar.append(this.places);
    const main = el('section', 'file-picker-main');
    const pathbar = el('div', 'file-picker-pathbar');
    const back = this.action('←', 'back', '이전 폴더');
    const parent = this.action('↑', 'up', '상위 폴더');
    this.backButton = back;
    this.upButton = parent;
    this.pathInput = el('input', 'file-picker-path');
    this.pathInput.setAttribute('aria-label', '폴더 경로');
    this.pathInput.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); this.navigate(this.pathInput.value); } });
    this.hiddenToggle = this.action('숨김 항목', 'hidden', '숨김 항목 표시');
    this.hiddenToggle.classList.add('file-picker-hidden');
    pathbar.append(back, parent, this.pathInput, this.hiddenToggle);
    const filter = el('div', 'file-picker-filter');
    this.filterInput = el('input', '', '');
    this.filterInput.placeholder = '현재 폴더에서 찾기';
    this.filterInput.setAttribute('aria-label', '현재 폴더에서 찾기');
    this.filterInput.addEventListener('input', () => this.renderEntries());
    filter.append(this.filterInput);
    this.status = el('p', 'file-picker-status', '불러오는 중…');
    this.list = el('div', 'file-picker-list');
    this.list.setAttribute('role', 'listbox');
    this.list.tabIndex = 0;
    this.list.addEventListener('keydown', event => this.onListKey(event));
    main.append(pathbar, filter, this.status, this.list);
    content.append(sidebar, main);
    const footer = el('footer', 'file-picker-footer');
    if (this.mode === 'create') {
      this.nameInput = el('input', 'file-picker-name');
      this.nameInput.placeholder = '예: notes.md';
      this.nameInput.setAttribute('aria-label', '새 파일 이름');
      this.nameInput.value = 'untitled.txt';
      this.nameInput.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); this.confirm(); } });
      footer.append(this.nameInput);
    }
    footer.append(this.action('취소', 'cancel', '취소'), this.action(this.mode === 'folder' ? '이 폴더 선택' : this.mode === 'create' ? '파일 만들기' : '열기', 'confirm', '선택 확인'));
    this.confirmButton = footer.querySelector('[data-picker-action=confirm]');
    this.confirmButton.dataset.pickerConfirm = 'true';
    this.dialog.append(content, footer);
    this.updateNavigation();
  }

  action(label, action, title) {
    const button = el('button', action === 'confirm' ? 'primary' : 'secondary', label);
    button.type = 'button';
    button.dataset.pickerAction = action;
    button.title = title;
    button.addEventListener('click', () => {
      if (action === 'cancel') this.finish(null);
      else if (action === 'back') this.goHistory(this.historyIndex - 1);
      else if (action === 'up') this.navigate(this.parentPath);
      else if (action === 'hidden') { this.hidden = !this.hidden; button.classList.toggle('active', this.hidden); this.navigate(this.path); }
      else if (action === 'confirm') this.confirm();
    });
    return button;
  }

  renderPlaces(data) {
    if (!this.dialog.isConnected) return;
    const places = [
      ['현재 작업 공간', this.options.workspace || this.options.cwd],
      ['홈', data.home], ['바탕 화면', data.desktop], ['문서', data.documents], ['다운로드', data.downloads],
      ...(this.options.recent || []).map(path => ['최근 · ' + path.split(/[\\/]/).filter(Boolean).at(-1), path]),
      ...(data.drives || []).map(drive => [drive.name, drive.path])
    ];
    this.places.replaceChildren();
    const seen = new Set();
    for (const [label, path] of places) {
      if (!path || seen.has(path.toLowerCase())) continue;
      seen.add(path.toLowerCase());
      const button = this.action(label, 'place', path);
      button.dataset.pickerPlace = path;
      button.addEventListener('click', () => this.navigate(path));
      this.places.append(button);
    }
  }

  goHistory(index) {
    if (index >= 0 && index < this.history.length) this.navigate(this.history[index], index);
  }

  async navigate(path, historyIndex = null) {
    if (!path || this.busy) return;
    const token = ++this.request;
    this.busy = true;
    this.loaded = false;
    this.updateNavigation();
    this.confirmButton.disabled = true;
    this.status.textContent = '불러오는 중…';
    this.status.className = 'file-picker-status';
    try {
      const result = await call('browse', { path, hidden: this.hidden, directoriesOnly: this.mode === 'folder' });
      if (token !== this.request || !this.dialog.isConnected) return;
      if (historyIndex === null && result.path !== this.path) {
        this.history = this.history.slice(0, this.historyIndex + 1);
        this.history.push(result.path);
        this.historyIndex = this.history.length - 1;
      } else if (historyIndex !== null) this.historyIndex = historyIndex;
      this.path = result.path;
      this.parentPath = result.parent;
      this.pathInput.value = result.path;
      this.entries = result.entries;
      this.rows.clear();
      this.truncated = result.truncated;
      this.selected = null;
      this.filterInput.value = '';
      this.loaded = true;
      this.renderEntries();
    } catch (error) {
      if (token !== this.request || !this.dialog.isConnected) return;
      this.pathInput.value = this.path;
      this.status.textContent = error.message;
      this.status.className = 'file-picker-status file-picker-error';
    } finally {
      if (token === this.request && this.dialog.isConnected) {
        this.busy = false;
        this.updateConfirm();
        this.updateNavigation();
      }
    }
  }

  visibleEntries() {
    const query = this.filterInput.value.trim().toLocaleLowerCase();
    return query ? this.entries.filter(entry => entry.name.toLocaleLowerCase().includes(query)) : this.entries;
  }

  renderEntries() {
    const entries = this.visibleEntries();
    this.list.replaceChildren();
    if (this.selected && !entries.some(entry => entry.path === this.selected.path)) this.selected = null;
    if (!entries.length) {
      this.status.textContent = this.entries.length ? '일치하는 항목이 없습니다.' : '이 폴더는 비어 있습니다.';
      this.updateConfirm();
      return;
    }
    this.status.textContent = this.truncated ? '처음 1,500개 항목만 표시합니다.' : `${entries.length}개 항목`;
    for (const entry of entries) {
      let row = this.rows.get(entry.path);
      if (!row) {
        row = el('button', 'file-picker-entry');
        row.type = 'button';
        row.dataset.path = entry.path;
        row.dataset.directory = String(entry.directory);
        row.setAttribute('role', 'option');
        row.append(icon(entry.directory ? 'folder' : 'file'), el('span', '', entry.name));
        row.title = entry.path;
        row.addEventListener('click', () => {
          if (this.busy) return;
          this.selected = entry;
          this.syncSelection();
        });
        row.addEventListener('dblclick', () => this.activate(entry));
        this.rows.set(entry.path, row);
      }
      this.list.append(row);
    }
    this.syncSelection();
    this.updateConfirm();
  }

  syncSelection() {
    for (const row of this.rows.values()) {
      const selected = row.dataset.path === this.selected?.path;
      row.classList.toggle('selected', selected);
      row.setAttribute('aria-selected', String(selected));
    }
    this.updateConfirm();
  }

  activate(entry) {
    if (this.busy || !this.loaded || !this.resolve) return;
    if (entry.directory) this.navigate(entry.path);
    else if (this.mode === 'file') this.finish(entry.path);
  }

  onListKey(event) {
    const entries = this.visibleEntries();
    const index = entries.findIndex(entry => entry.path === this.selected?.path);
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const next = Math.max(0, Math.min(entries.length - 1, index < 0 ? 0 : index + (event.key === 'ArrowDown' ? 1 : -1)));
      this.selected = entries[next] || null;
      this.syncSelection();
      this.list.querySelector('.selected')?.focus();
    } else if (event.key === 'Enter' && this.selected) {
      event.preventDefault(); this.activate(this.selected);
    }
  }

  updateConfirm() {
    if (!this.confirmButton) return;
    this.confirmButton.disabled = this.busy || !this.loaded || (this.mode === 'file' && !this.selected);
  }

  updateNavigation() {
    if (!this.backButton) return;
    this.backButton.disabled = this.busy || this.historyIndex <= 0;
    this.upButton.disabled = this.busy || !this.parentPath;
  }

  async confirm() {
    if (this.busy || !this.loaded || !this.resolve) return;
    if (this.mode === 'folder') return this.finish(this.selected?.directory ? this.selected.path : this.path);
    if (this.mode === 'file') {
      if (!this.selected) return;
      return this.selected.directory ? this.navigate(this.selected.path) : this.finish(this.selected.path);
    }
    const name = this.nameInput.value.trim();
    if (!name || /[\\/:*?"<>|]/.test(name)) { this.status.textContent = '유효한 새 파일 이름을 입력해 주세요.'; this.status.className = 'file-picker-status file-picker-error'; return; }
    this.busy = true;
    this.updateConfirm();
    try {
      const result = await call('createFile', { path: join(this.path, name) });
      if (this.resolve) this.finish(result.path);
    }
    catch (error) { this.status.textContent = error.message; this.status.className = 'file-picker-status file-picker-error'; this.busy = false; this.updateConfirm(); }
  }

  finish(value) {
    if (!this.resolve) return;
    const resolve = this.resolve;
    this.resolve = null;
    this.request++;
    if (this.dialog.isConnected) { this.dialog.close(); this.dialog.remove(); }
    this.previousFocus?.focus?.();
    resolve(value);
  }
}
