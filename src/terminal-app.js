import './terminal-app.css';
import { call, notify, on, isDesktop } from './bridge.js';
import { TerminalPane } from './terminal.js';
import { icon } from './ui.js';

const app = document.querySelector('#app');
app.innerHTML = `
  <main class="terminal-app">
    <header class="terminal-titlebar">
      <div class="terminal-brand"><img src="./orbit.svg" alt=""><span>Orbit</span></div>
      <div class="terminal-title-actions"><span class="terminal-status" id="terminal-status" role="status">시작 중</span><div class="terminal-window-controls" aria-label="창 제어"><button id="window-minimize" title="최소화" aria-label="최소화">${icon('minimize').innerHTML}</button><button id="window-maximize" title="최대화 또는 복원" aria-label="최대화 또는 복원">${icon('maximize').innerHTML}</button><button id="window-close" class="close" title="닫기" aria-label="닫기">${icon('close').innerHTML}</button></div></div>
    </header>
    <div class="terminal-toolbar"><label for="shell-profile">Shell</label><select id="shell-profile" aria-label="셸 선택"><option value="powershell">PowerShell</option><option value="cmd">Command Prompt</option></select><button id="restart-terminal" type="button">Restart</button><span class="terminal-cwd" id="terminal-cwd"></span></div>
    <section id="terminal-host" class="terminal-host" aria-label="터미널"></section>
    <div id="terminal-error" class="terminal-error" role="alert" hidden></div>
  </main>`;

const status = document.querySelector('#terminal-status');
const terminalHost = document.querySelector('#terminal-host');
const errorBox = document.querySelector('#terminal-error');
const profileSelect = document.querySelector('#shell-profile');
let pane = null;
let folder = '';
let settings = { fontSize: 14, lowPower: true, theme: 'dark' };
let starting = false;
let initialized = false;
const setStatus = text => { status.textContent = text; };

document.querySelector('#window-minimize').onclick = () => notify('windowMinimize');
document.querySelector('#window-maximize').onclick = () => notify('windowMaximize');
document.querySelector('#window-close').onclick = () => notify('windowClose');
document.querySelector('#restart-terminal').onclick = () => initialized ? startTerminal() : initialize();
profileSelect.onchange = () => {};

function browserSample() {
  terminalHost.innerHTML = '<div class="preview-terminal"><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> Get-Location</div><pre>Path\n----\nC:\\Workspace</pre><div><span class="preview-prompt">PS C:\\Workspace&gt;</span> <span class="preview-cursor"></span></div><p>Browser preview · sample terminal output. Shell commands run in the Windows desktop app.</p></div>';
  setStatus('미리보기');
  document.querySelector('#restart-terminal').disabled = true;
  profileSelect.disabled = true;
}

async function startTerminal() {
  if (!isDesktop || starting) return;
  starting = true;
  document.querySelector('#restart-terminal').disabled = true;
  profileSelect.disabled = true;
  errorBox.hidden = true;
  terminalHost.replaceChildren();
  if (pane) notify('closeTerminal', { session: pane.id });
  pane?.dispose();
  pane = null;
  setStatus('셸 시작 중');
  const id = `terminal-${crypto.randomUUID()}`;
  try {
    pane = new TerminalPane({
      id, profile: profileSelect.value, cwd: folder, name: profileSelect.selectedOptions[0].textContent,
      settings, openLink: url => call('external', { path: url, cwd: folder }), workspaceShortcuts: false,
      onExit: code => { setStatus(`종료 · ${code}`); document.querySelector('#restart-terminal').disabled = false; profileSelect.disabled = false; },
      onFocus: () => {},
    });
    terminalHost.append(pane.element);
    await pane.start();
    setStatus(profileSelect.selectedOptions[0].textContent);
    pane.focus();
  } catch (error) {
    pane?.dispose();
    pane = null;
    terminalHost.replaceChildren();
    errorBox.textContent = `터미널을 시작하지 못했습니다. ${error.message}`;
    errorBox.hidden = false;
    setStatus('시작 실패');
  } finally {
    starting = false;
    document.querySelector('#restart-terminal').disabled = false;
    profileSelect.disabled = false;
  }
}

async function initialize() {
  if (!isDesktop) { browserSample(); return; }
  try {
    const data = await call('init');
    folder = typeof data.folder === 'string' ? data.folder : '';
    settings = { ...settings, ...(data.settings || {}), theme: 'dark' };
    settings.fontSize = Math.max(11, Math.min(20, Number(settings.fontSize) || 14));
    initialized = true;
    document.querySelector('#terminal-cwd').textContent = folder;
    document.title = 'Orbit · Terminal';
    await startTerminal();
  } catch (error) {
    errorBox.textContent = `Orbit을 초기화하지 못했습니다. ${error.message}`;
    errorBox.hidden = false;
    setStatus('시작 실패');
  }
}

// Keep a small native smoke-test surface and the remote test harness contract.
window.orbitDiagnostics = () => ({ ready: initialized && !!pane?.started, windowControls: 3, profile: pane?.profile || '', cwd: folder, cols: pane?.term.cols || 0, rows: pane?.term.rows || 0, sessions: pane ? [{ id: pane.id, profile: pane.profile, pid: pane.pid, exited: pane.exited }] : [] });
window.orbitUiTest = {
  startCmd: async () => { profileSelect.value = 'cmd'; await startTerminal(); },
  startPowerShell: async () => { profileSelect.value = 'powershell'; await startTerminal(); },
  send: async data => { if (!pane) throw new Error('terminal is not running'); notify('write', { session: pane.id, data }); },
  diagnostics: () => ({ ready: true, sessions: pane ? [{ id: pane.id, profile: pane.profile, pid: pane.pid, exited: pane.exited }] : [], output: pane ? Array.from({ length: pane.term.buffer.active.length }, (_, i) => pane.term.buffer.active.getLine(i)?.translateToString(true) || '').join('\n') : '' }),
};
on('error', data => { errorBox.textContent = data.message; errorBox.hidden = false; });
window.addEventListener('beforeunload', () => pane?.dispose());
initialize();
