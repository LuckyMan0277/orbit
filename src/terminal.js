import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { SearchAddon } from '@xterm/addon-search';
import { SerializeAddon } from '@xterm/addon-serialize';
import '@xterm/xterm/css/xterm.css';
import { call, notify, on } from './bridge.js';
import { extractLinks } from './links.js';
import { el, toast, confirm } from './ui.js';
import { prepareTerminalPaste } from './terminal-paste.js';

const terminalTheme = name => name === 'light'
  ? { background: '#fcfbff', foreground: '#30313b', cursor: '#50307c', selectionBackground: '#d8cbed99', black: '#30313b', red: '#b43d57', green: '#2f754c', yellow: '#766016', blue: '#28629a', magenta: '#71499c', cyan: '#287278', white: '#5a5363', brightBlack: '#756d7e', brightRed: '#8f253f', brightGreen: '#1e653c', brightYellow: '#63500d', brightBlue: '#1d548b', brightMagenta: '#5f358f', brightCyan: '#17656d', brightWhite: '#3f3947' }
  : { background: '#15171c', foreground: '#d5d9e2', cursor: '#b8a1ff', selectionBackground: '#7660ae70', black: '#252833', red: '#f38ba8', green: '#a6d7ad', yellow: '#e6ca8b', blue: '#8db8ee', magenta: '#c8aff0', cyan: '#93d6db', white: '#e4e7ee', brightBlack: '#7f8798' };
export class TerminalPane {
  constructor({ id, profile, cwd, resumeId, name, settings, openLink, onExit, onFocus, attach }) {
    this.id = id; this.cwd = cwd; this.profile = profile; this.resumeId = resumeId || ''; this.name = name || profile; this.closed = false; this.exited = false; this.started = false; this.outputCount = 0;
    // Attached: a view of a terminal the host opened. It keeps the host terminal's size (never resizes it) and starts from the host's snapshot.
    this.attach = attach || null; this.attached = !!attach;
    this.element = el('div', attach ? 'terminal-pane attached' : 'terminal-pane');
    this.term = new Terminal({ fontFamily: '"Cascadia Mono", "Cascadia Code", Consolas, "Malgun Gothic", monospace', fontSize: settings.fontSize, lineHeight: 1.24, scrollback: settings.lowPower ? 500 : 2000, cursorBlink: false, allowProposedApi: false, convertEol: false, minimumContrastRatio: settings.theme === 'light' ? 4.5 : 1, theme: terminalTheme(settings.theme), linkHandler: { activate: (e, text) => openLink(text, this.cwd), allowNonHttpProtocols: true } });
    this.fit = new FitAddon(); this.search = new SearchAddon(); this.serialize = new SerializeAddon(); this.term.loadAddon(this.fit); this.term.loadAddon(this.search); this.term.loadAddon(this.serialize);
    this.term.open(this.element);
    this.term.onData(data => { if (this.started && !this.exited) notify('write', { session: this.id, data }); });
    this.term.onResize(({ cols, rows }) => { if (this.started && !this.exited && !this.attached) notify('resize', { session: this.id, cols, rows }); });
    this.term.onBell(() => { if (document.hidden) document.title = '• 터미널 알림 — Orbit'; });
    this.element.addEventListener('pointerdown', onFocus);
    this.element.addEventListener('contextmenu', event => { event.preventDefault(); void this.pasteClipboard(); });
    this.term.attachCustomKeyEventHandler(event => {
      if (event.type !== 'keydown') return true;
      if (event.ctrlKey && event.shiftKey && event.code === 'KeyC') { call('clipboard', { text: this.term.getSelection() }).catch(e => toast(e.message, true)); return false; }
      if (event.ctrlKey && event.code === 'KeyV') { void this.pasteClipboard(); return false; }
      // Keep workspace shortcuts from becoming control characters in the CLI.
      if (event.ctrlKey && ['KeyO', 'KeyS', 'KeyK', 'KeyP', 'KeyB'].includes(event.code)) return false;
      if (event.ctrlKey && event.shiftKey && ['KeyT','KeyW','KeyD','KeyF'].includes(event.code)) return false;
      return true;
    });
    this.term.registerLinkProvider({ provideLinks: (lineNumber, callback) => {
      const buffer = this.term.buffer.active;
      let first = lineNumber - 1;
      while (first > 0 && buffer.getLine(first)?.isWrapped) first--;
      let last = lineNumber - 1;
      while (buffer.getLine(last + 1)?.isWrapped) last++;
      // A logical line can wrap over multiple rows. Keep terminal-cell offsets for wide Korean glyphs.
      let text = ''; const positions = [];
      for (let y = first; y <= last; y++) {
        const line = buffer.getLine(y);
        if (!line) continue;
        for (let x = 0; x < line.length; x++) {
          const cell = line.getCell(x); if (!cell || cell.getWidth() === 0) continue;
          const chars = cell.getChars() || ' ';
          for (let c = 0; c < chars.length; c++) positions.push({ x: x + 1, y: y + 1, end: x + Math.max(1, cell.getWidth()) });
          text += chars;
        }
      }
      callback(extractLinks(text).map(link => {
        const start = positions[link.start], end = positions[link.end - 1];
        if (!start || !end || start.y > lineNumber || end.y < lineNumber) return null;
        return { range: { start: { x: start.x, y: start.y }, end: { x: end.end, y: end.y } }, text: link.value, activate: () => openLink(link.value, this.cwd) };
      }).filter(Boolean));
    } });
    this.unsub = on('output', data => {
      if (data.session !== this.id) return;
      if (this.attached && data.seq && data.seq <= (this.lastProcessedSeq || 0)) return; // already part of the snapshot
      this.outputCount += data.data.length;
      this.term.write(data.data, () => { this.lastProcessedSeq = data.seq || this.lastProcessedSeq || 0; notify('ack', { session: this.id }); });
    });
    this.unsnapshot = on('remoteSnapshot', data => { if(data.session === this.id) this.term.write('', () => notify('remoteSnapshot', { session: this.id, request:data.request, data: this.serialize.serialize(), seq: this.lastProcessedSeq || 0, cols: this.term.cols, rows: this.term.rows })); });
    this.unexit = on('exit', data => { if (data.session === this.id && !this.closed) { this.exited = true; this.term.write(`\r\n\x1b[90m[프로세스 종료 · 코드 ${data.code}]\x1b[0m\r\n`); onExit(data.code); } });
    this.observer = new ResizeObserver(() => { if (!this.attached && this.element.clientWidth && this.element.clientHeight) this.fit.fit(); });
    this.observer.observe(this.element);
  }
  async start() {
    if (this.attached) {
      const { cols, rows, data, seq } = this.attach;
      this.term.resize(Math.max(10, cols || 80), Math.max(3, rows || 24));
      this.lastProcessedSeq = seq || 0; this.started = true;
      if (data) await new Promise(resolve => this.term.write(data, resolve));
      return;
    }
    this.fit.fit();
    const result = await call('createTerminal', { session: this.id, cwd: this.cwd, profile: this.profile, resumeId: this.resumeId, name: this.name, cols: this.term.cols, rows: this.term.rows });
    this.started = true; this.pid = result.pid; this.focus();
  }
  focus() { requestAnimationFrame(() => { if (!this.closed && this.element.clientWidth) { if (!this.attached) this.fit.fit(); this.term.focus(); } }); }
  configure(settings) { this.term.options.fontSize = settings.fontSize; this.term.options.scrollback = settings.lowPower ? 500 : 2000; this.term.options.minimumContrastRatio = settings.theme === 'light' ? 4.5 : 1; this.term.options.theme = terminalTheme(settings.theme); if (!this.attached) this.fit.fit(); }
  async pasteClipboard() {
    if (this.exited || !this.started) return toast('실행 중인 터미널을 선택해 주세요.', true);
    const text = await call('clipboardRead');
    if (text) await this.paste(text);
  }
  async paste(text) {
    if (this.exited || !this.started) return toast('실행 중인 터미널을 선택해 주세요.', true);
    const prepared = prepareTerminalPaste(text, this.term.modes.bracketedPasteMode);
    this.term.paste(prepared.text);
    if (prepared.collapsed) toast('줄바꿈은 실행되지 않도록 공백으로 붙여넣었습니다.');
    this.focus();
  }
  dispose() { this.closed = true; this.unsub(); this.unexit(); this.unsnapshot(); this.observer.disconnect(); this.term.dispose(); this.element.remove(); }
}
