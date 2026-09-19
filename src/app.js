import './styles.css';
import { $, el, button, icon, iconButton, toast, guard, dialog, confirm } from './ui.js';
import { call, notify, on, isDesktop } from './bridge.js';
import { parseLocation } from './links.js';

const state = { folder: '', terminals: [], terminalCreating: 0, active: null, terminalRoot: { type: 'group', id: 'group-root', tabs: [], active: null }, activeGroup: 'group-root', files: [], file: null, editor: null, editorModule: null, markdownEpoch: 0, hidden: false, savedSessions: [], savedAll: false, savedEpoch: 0, settings: { lowPower: true, fontSize: 13, defaultShell: 'powershell', theme: 'dark', recent: [], sidebarMode: 'saved' }, loadingFile: false };
const profiles = { codex: { name: 'Codex', subtitle: 'OpenAI CLI', mark: '✳', class: 'codex' }, claude: { name: 'Claude', subtitle: 'Anthropic CLI', mark: '✺', class: 'claude' }, powershell: { name: 'PowerShell', subtitle: '기본 터미널', mark: '>_', class: 'shell' }, cmd: { name: 'CMD', subtitle: '명령 프롬프트', mark: '>_', class: 'shell' } };
const basename = path => path.split(/[\\/]/).filter(Boolean).at(-1) || path;
const projectKey = path => String(path || '').toLowerCase();
const projectName = path => state.client ? (state.client.names[path] || basename(path)) : (state.settings.projectNames?.[projectKey(path)] || basename(path));
const app = $('#app');
app.innerHTML = `
  <section class="home-screen" id="home-screen" aria-label="Orbit 홈">
    <header class="home-header"><div class="home-brand"><img class="orbit-wordmark-mark" src="./orbit.svg" alt=""><span>orbit</span></div><div class="home-header-actions"><button class="external-connection update-available" data-update hidden></button><button id="home-pick-folder" class="home-open-folder">프로젝트 추가</button><button id="home-client-refresh" class="home-open-folder client-only">새로 고침</button><button id="home-client-logout" class="home-open-folder client-only">로그아웃</button><div class="window-controls home-window-controls" aria-label="창 제어"><button id="home-window-minimize" title="최소화" aria-label="최소화"></button><button id="home-window-maximize" title="최대화 또는 복원" aria-label="최대화 또는 복원"></button><button id="home-window-close" class="window-close" title="닫기" aria-label="닫기"></button></div></div></header>
    <main class="home-stage"><section class="home-orbit-system" id="home-orbit" aria-label="최근 프로젝트"></section><div class="home-recent-note"><small>프로젝트 점을 선택해 작업 공간을 엽니다.</small></div></main>
  </section>
  <aside class="sidebar">
    <div class="brand"><img class="orbit-wordmark-mark" src="./orbit.svg" alt=""><div>orbit<span class="brand-caption">AGENT WORKSPACE</span></div><span class="version" id="app-version"></span></div>
    <button class="workspace-picker" id="pick-folder"><span class="folder-symbol"></span><span class="workspace-name"><small>WORKSPACE</small><strong id="folder-name">불러오는 중…</strong></span><span class="picker-arrow" id="picker-arrow"></span></button>
    <div class="side-heading"><span>탐색기</span><div id="tree-actions"></div></div>
    <div class="file-filter"><span id="filter-icon"></span><input id="file-filter" placeholder="표시된 파일 필터…" aria-label="표시된 파일 필터"></div>
    <div id="file-tree" class="file-tree" aria-label="파일 탐색기"></div>
    <div class="sidebar-bottom"><div class="side-heading"><span>빠른 도구</span><span class="tiny">WORKFLOW</span></div><div id="quick-tools"></div><div class="resource-card" id="resource-card"><span id="resource-leaf"></span><div><strong id="power-label">가볍게 실행 중</strong><small id="power-detail">절약 모드 · 스크롤 기록 500줄</small></div><span class="status-dot"></span></div><button id="preferences" class="settings-button"><span id="settings-icon"></span>설정 및 사용량<span>⌘</span></button></div>
  </aside>
  <main class="main">
    <header class="topbar"><div class="breadcrumb"><button id="home-return" class="home-return" title="Home" aria-label="Return to Home">orbit</button><button id="sidebar-toggle" class="icon-button" title="사이드바 토글 (Ctrl+B)" aria-label="사이드바 토글"></button><span id="breadcrumb-folder">workspace</span><span class="slash">/</span><span>작업 공간</span><span class="terminal-mini">터미널 <span id="session-count">0</span></span></div><div class="topbar-right"><button class="external-connection update-available" data-update hidden></button><button id="external-connection" class="external-connection" aria-label="기기 연결" title="기기 연결 · 계정 로그인"><span id="external-icon"></span><span>외부 연결</span><small id="external-state">꺼짐</small></button><div class="toolbar-actions" id="workspace-actions"></div><button class="command-trigger" id="command-button">명령 찾기 <kbd>Ctrl K</kbd></button><div class="window-controls" aria-label="창 제어"><button id="window-minimize" title="최소화" aria-label="최소화"></button><button id="window-maximize" title="최대화 또는 복원" aria-label="최대화 또는 복원"></button><button id="window-close" class="window-close" title="닫기" aria-label="닫기"></button></div></div></header>
    <div class="work-area" id="work-area">
      <section class="terminal-section" id="terminal-section">
        <div id="welcome" class="welcome">
          <div class="welcome-copy workspace-welcome-copy"><span class="welcome-kicker">WORKSPACE READY</span><h1>작업을<br><span>시작하세요.</span></h1><p>사이드바에서 파일과 저장된 대화를 확인하거나, 아래에서 새 터미널을 시작하세요.</p></div>
          <section class="workspace-launch-section" aria-label="새 작업 시작"><div class="workspace-launch-heading"><span>새 작업 시작</span><small>원하는 에이전트를 엽니다</small></div><div class="launcher-grid" id="launchers"></div></section>
          <div class="welcome-foot"><span><i class="status-dot"></i> 준비됐어요. 새 터미널을 시작하거나 작업을 이어가세요.</span><span>새 터미널 <kbd>Ctrl Shift T</kbd></span></div>
        </div><div id="terminal-layout" class="terminal-layout"></div></section>
      <div id="editor-resizer" class="resizer" role="separator" aria-label="편집기 너비 조절" tabindex="0"></div>
      <section class="editor-section" id="editor-section" hidden><div class="editor-tabs" id="editor-tabs"></div><div class="editor-toolbar"><span id="editor-path"></span><div id="editor-actions"></div></div><div id="editor-host" class="editor-host"></div><article id="markdown-preview" class="markdown-preview" hidden></article><div class="editor-footer"><span id="editor-encoding">UTF-8</span><span id="editor-cursor">Ln 1, Col 1</span><span id="editor-language">Text</span></div></section>
    </div>
    <footer class="statusbar"><div><span class="status-dot"></span><span id="status-text">준비</span><span class="status-divider"></span><span id="status-folder"></span></div><div><span id="status-sessions">0개 세션</span><span class="status-divider"></span><span>로컬 파일 · 로컬 실행</span><span class="status-orbit">orbit</span></div></footer>
  </main>`;

app.classList.add('home-active');

$('.folder-symbol').append(icon('folder')); $('#filter-icon').append(icon('search')); $('#resource-leaf').append(icon('leaf')); $('#settings-icon').append(icon('settings'));
$('#picker-arrow').append(icon('chevronDown'));
$('#sidebar-toggle').append(icon('menu'));
for (const [id, name] of [['home-window-minimize','minimize'],['home-window-maximize','maximize'],['home-window-close','close'],['window-minimize','minimize'],['window-maximize','maximize'],['window-close','close']]) $('#'+id).append(icon(name));
const savedHeading = el('div','side-heading'); savedHeading.append(el('span','','저장된 대화'));
const savedRefresh = iconButton('refresh','저장된 대화 새로고침',guard(refreshSavedSessions));
savedHeading.append(savedRefresh);
const savedNewRow=el('div','saved-new-row');savedNewRow.append(button('새 Codex','sidebar-new codex-new',guard(()=>newTerminal('codex')),'새 Codex 세션'),button('새 Claude','sidebar-new claude-new',guard(()=>newTerminal('claude')),'새 Claude 세션'));
const savedSearch = el('input'); savedSearch.placeholder='대화 검색'; savedSearch.setAttribute('aria-label','저장된 대화 검색'); savedSearch.oninput=renderSavedSessions;
const savedFilter = el('div','file-filter'); savedFilter.append(icon('search'),savedSearch);
const savedList = el('div','saved-sessions'); savedList.id='saved-sessions';
const fileFilter=$('#file-filter').parentElement, fileTree=$('#file-tree'), treeHeading=$('#tree-actions').parentElement;
const sidebar=$('.sidebar'), sidebarBottom=$('.sidebar-bottom');
const sidebarTabs=el('div','sidebar-mode-tabs'); sidebarTabs.id='sidebar-mode-tabs'; sidebarTabs.setAttribute('role','tablist'); sidebarTabs.setAttribute('aria-label','사이드바 보기');
const savedTab=button('저장된 대화','sidebar-mode-tab active',()=>setSidebarMode('saved')); savedTab.id='sidebar-saved-tab'; savedTab.setAttribute('role','tab'); savedTab.setAttribute('aria-controls','saved-conversations-panel');
const explorerTab=button('파일 탐색기','sidebar-mode-tab',()=>setSidebarMode('explorer')); explorerTab.id='sidebar-explorer-tab'; explorerTab.setAttribute('role','tab'); explorerTab.setAttribute('aria-controls','explorer-panel');
sidebarTabs.append(savedTab,explorerTab);
const sidebarPanels=el('div','sidebar-panels');
const savedPanel=el('section','sidebar-panel saved-panel'); savedPanel.id='saved-conversations-panel'; savedPanel.setAttribute('role','tabpanel'); savedPanel.setAttribute('aria-labelledby',savedTab.id); savedPanel.append(savedHeading,savedNewRow,savedFilter,savedList);
const explorerPanel=el('section','sidebar-panel explorer-panel'); explorerPanel.id='explorer-panel'; explorerPanel.setAttribute('role','tabpanel'); explorerPanel.setAttribute('aria-labelledby',explorerTab.id); explorerPanel.append(treeHeading,fileFilter,fileTree);
sidebarPanels.append(savedPanel,explorerPanel); sidebar.insertBefore(sidebarTabs,sidebarBottom); sidebar.insertBefore(sidebarPanels,sidebarBottom);
function setSidebarMode(mode, save=true) {
  const explorer=mode==='explorer';
  savedTab.classList.toggle('active',!explorer); explorerTab.classList.toggle('active',explorer);
  savedTab.setAttribute('aria-selected',String(!explorer)); explorerTab.setAttribute('aria-selected',String(explorer));
  savedTab.tabIndex=explorer?-1:0; explorerTab.tabIndex=explorer?0:-1;
  savedPanel.hidden=explorer; explorerPanel.hidden=!explorer;
  state.settings.sidebarMode=explorer?'explorer':'saved';
  if(save) persist();
}
sidebarTabs.onkeydown=event=>{
  const tabs=[savedTab,explorerTab];
  const current=tabs.indexOf(document.activeElement);
  if(current<0)return;
  let next=null;
  if(event.key==='ArrowLeft')next=(current+tabs.length-1)%tabs.length;
  if(event.key==='ArrowRight')next=(current+1)%tabs.length;
  if(event.key==='Home')next=0;
  if(event.key==='End')next=tabs.length-1;
  if(next!==null){event.preventDefault();tabs[next].focus();setSidebarMode(next?'explorer':'saved');}
};
setSidebarMode('saved',false);
$('#pick-folder').onclick = guard(pickFolder);
$('#home-pick-folder').onclick = guard(pickHomeProject);
$('#home-client-refresh').onclick = guard(refreshClientProjects);
$('#home-client-logout').onclick = guard(leaveClientMode);
$('#home-window-minimize').onclick = () => notify('windowMinimize');
$('#home-window-maximize').onclick = () => notify('windowMaximize');
$('#home-window-close').onclick = () => notify('windowClose');
$('#home-return').onclick = showHome;
$('#sidebar-toggle').onclick = () => app.classList.toggle('sidebar-hidden');
$('#preferences').onclick = guard(preferences);
$('#external-icon').append(icon('devices'));$('#external-connection').onclick=guard(remoteDevices);
$('#command-button').onclick = commands;
$('#window-minimize').onclick = () => notify('windowMinimize');
$('#window-maximize').onclick = () => notify('windowMaximize');
$('#window-close').onclick = () => notify('windowClose');
$('#tree-actions').append(iconButton('refresh', '파일 목록 새로고침', guard(refreshTree)), iconButton('plus', '새 파일', guard(newFile)));
$('#workspace-actions').append(
  iconButton('search', '터미널 검색 (Ctrl+Shift+F)', terminalSearch),
  iconButton('clipboard', '클립보드를 터미널에 붙여넣기 (Ctrl+V)', guard(pasteClipboardToActive)),
  iconButton('split', '터미널 분할 보기 (Ctrl+Shift+D)', guard(toggleSplit))
);
{const preview=button('미리보기','editor-mode',()=>setMarkdownMode('preview')),edit=button('편집','editor-mode',()=>setMarkdownMode('edit'));preview.dataset.editorMode='preview';edit.dataset.editorMode='edit';$('#editor-actions').append(preview,edit,iconButton('terminal', '파일 경로를 터미널에 붙여넣기', guard(() => pasteActive(`"${state.file.path}"`))), iconButton('refresh', '디스크에서 다시 열기', guard(reloadFile)), button('저장', 'save-button', guard(saveFile)));}
for (const profile of ['codex','claude','powershell']) {
  const p = profiles[profile]; const card = button('', `launcher ${p.class}`, guard(() => newTerminal(profile))); card.dataset.profile = profile;
  card.append(el('span', 'launcher-mark', p.mark)); const copy = el('div'); copy.append(el('strong', '', p.name), el('small', '', p.subtitle)); card.append(copy, icon('arrow')); $('#launchers').append(card);
}
for (const [name, label, action, shortcut] of [['file','파일 열기',openFilePicker,'Ctrl O'],['bookmark','프롬프트 보관함',promptLibrary,''],['folder','최근 작업 폴더',recentFolders,'']]) {
  const b = button('', 'quick-tool', guard(action)); b.title=label+(shortcut?` (${shortcut})`:'');b.setAttribute('aria-label',label);b.append(icon(name), el('span','',label), el('kbd','',shortcut)); $('#quick-tools').append(b);
}
$('#file-filter').addEventListener('input', () => { const query = $('#file-filter').value.toLowerCase(); for (const item of $('#file-tree').querySelectorAll('.tree-file')) item.hidden = !item.dataset.name.includes(query); });
on('error', data => toast(data.message, true));
on('remoteCreate', async data => { try { if (!['codex','claude','powershell','cmd'].includes(data.profile)) throw new Error('지원하지 않는 터미널입니다.'); let resume=null;if(data.resumeId){if(!data.cwd||!data.title)throw new Error('저장된 대화 정보가 올바르지 않습니다.');const listed=await call('savedSessions',{cwd:state.folder,allWorkspaces:true}),items=Array.isArray(listed.sessions)?listed.sessions:[];resume=items.find(x=>x.provider===data.profile&&x.id===data.resumeId&&x.cwd===data.cwd);if(!resume)throw new Error('저장된 대화 정보를 다시 확인할 수 없습니다.');if(!matchesProject(resume.cwd,state.folder))await setFolder(resume.cwd);const open=state.terminals.find(t=>t.profile===resume.provider&&t.resumeId===resume.id&&!t.pane.exited);if(open){activateTerminal(open.id);open.pane.focus();notify('remoteCreate',{request:data.request,session:open.id});return;}} else if(data.project){if(!visibleProjects().some(path=>matchesProject(path,data.project)))throw new Error('지원하지 않는 프로젝트입니다.');if(!matchesProject(data.project,state.folder))await setFolder(data.project);resume={cwd:data.project};} const result=await newTerminal(data.profile,resume&&{resumeId:resume.resumeId,cwd:resume.cwd,title:resume.title}); notify('remoteCreate',{request:data.request,session:result?.session||''}); } catch(e) { if(data.project&&e.message==='작업 폴더를 찾을 수 없습니다.'&&!matchesProject(data.project,state.folder)){state.settings.recent=state.settings.recent.filter(value=>!matchesProject(value,data.project));persist();} notify('remoteCreate',{request:data.request,error:e.message}); } });
on('remoteProjects', data => { notify('remoteProjects', { request:data.request, projects: visibleProjects().map(path => ({ path, name: projectName(path) })), current: state.folder }); });
document.addEventListener('visibilitychange', () => { if (!document.hidden) document.title = 'Orbit · Agent workspace'; });

async function initialize() {
  const data = await call('init');
  Object.assign(state.settings, data.settings || {});
  setSidebarMode(state.settings.sidebarMode==='explorer'?'explorer':'saved',false);
  state.settings.fontSize = Math.max(11,Math.min(20,Number(state.settings.fontSize) || 13));
  state.settings.recent = Array.isArray(state.settings.recent) ? state.settings.recent.filter(x => typeof x === 'string').slice(0,8) : [];
  // This is deliberately UI-only state.  It hides a removed project from the
  // Home constellation without touching the project's folder or its files.
  state.settings.removedProjects = Array.isArray(state.settings.removedProjects) ? state.settings.removedProjects.filter(x => typeof x === 'string').slice(0,16) : [];
  state.settings.projectNames = state.settings.projectNames && typeof state.settings.projectNames === 'object' && !Array.isArray(state.settings.projectNames) ? Object.fromEntries(Object.entries(state.settings.projectNames).filter(([key, value]) => typeof key === 'string' && typeof value === 'string').slice(0, 16)) : {};
  state.settings.lowPower = state.settings.lowPower !== false;
  state.settings.defaultShell = ['powershell','cmd'].includes(state.settings.defaultShell) ? state.settings.defaultShell : 'powershell';
  state.settings.theme = ['dark', 'light'].includes(state.settings.theme) ? state.settings.theme : 'dark';
  state.settings.remoteAutoConnect = state.settings.remoteAutoConnect === true;
  applyTheme();
  if (isDesktop && typeof state.settings.remoteOrigin === 'string' && state.settings.remoteOrigin) call('remotePublicOrigin', { url: state.settings.remoteOrigin }).catch(()=>{});
  if (isDesktop) { const updateRemoteBadge=s=>{$('#external-state').textContent=!s.enabled?'꺼짐':s.url?.startsWith('https:')?'외부 준비':'이 PC';};call('remoteStatus').then(updateRemoteBadge).catch(()=>{});on('remoteStatus',data=>updateRemoteBadge(data.status)); } else { $('#status-text').textContent = '터미널 실행은 데스크톱 앱에서'; }
  if (isDesktop && state.settings.remoteAutoConnect && !data.testMode) call('remoteConnectStart',{port:49821}).then(result=>{const s=result?.status;if(s){const badge=$('#external-state');badge.textContent=!s.enabled?'꺼짐':s.url?.startsWith('https:')?'외부 준비':'이 PC';}}).catch(error=>{const badge=$('#external-state');badge.textContent='연결 확인';badge.title=`자동 연결 실패: ${error.message}. 클릭하여 다시 연결하세요.`;$('#external-connection').title=badge.title;});
  if (typeof data.version === 'string') $('#app-version').textContent = data.version;
  if (isDesktop && !data.testMode) checkForUpdate();
  // A PC that is already linked as a host has no reason to ask; anywhere else the
  // first thing offered is logging in to another PC.
  if (isDesktop && !data.testMode && !state.settings.startupLoginHidden) call('accountStatus').then(a=>{if(!a.linked)showLoginScreen();}).catch(()=>{});
  state.folder = typeof data.folder === 'string' ? data.folder : '';
  // The native host supplies its launch folder on every start. Respect a
  // deliberate Home removal so that folder does not quietly return on reload.
  if (state.settings.removedProjects.some(path => matchesProject(path, state.folder))) state.folder = '';
  renderHomeOrbit();
  updatePower();
}
async function setFolder(path) {
  if (!path) return;
  showWorkspace();
  state.folder = path;
  for (const id of ['folder-name','breadcrumb-folder']) $("#" + id).textContent = basename(path);
  $('#status-folder').textContent = path; $('#pick-folder').title = path;
  const matches = value => typeof value === 'string' && value.toLowerCase() === path.toLowerCase();
  state.settings.recent = [path, ...state.settings.recent.filter(x => !matches(x))].slice(0,8);
  state.settings.removedProjects = state.settings.removedProjects.filter(x => !matches(x));
  persist();
  renderHomeOrbit();
  await refreshTree();
  await refreshSavedSessions();
}
function showHome() {
  app.classList.add('home-active');
  renderHomeOrbit();
}
function showWorkspace() {
  app.classList.remove('home-active');
  renderTerminals();
}
function renderHomeOrbit() {
  const system = $('#home-orbit');
  if (!system) return;
  const projects = visibleProjects()
    .slice(0, 8);
  system.replaceChildren();
  const core = el('div', 'home-orbit-core');
  core.setAttribute('aria-hidden', 'true');
  const planet = el('span', 'home-machine-planet');
  core.append(planet);
  system.append(core);
  const labelLayer = el('div', 'home-project-label-layer');
  system.append(labelLayer);
  if (!projects.length) {
    const empty = state.client ? button('호스트에 프로젝트가 없습니다 · 새로 고침', 'home-empty-project', guard(refreshClientProjects)) : button('첫 프로젝트 추가', 'home-empty-project', guard(pickHomeProject));
    system.append(empty);
    return;
  }
  // Each workspace owns one visible ellipse and one node that follows that ellipse.
  // Keep the geometry bounded so the same model works on the compact Home layout.
  const orbits = [
    ['clamp(138px, 33vw, 340px)', 'clamp(62px, 15vw, 150px)', -27, 37, -8],
    ['clamp(116px, 28vw, 270px)', 'clamp(52px, 12.5vw, 116px)', 48, 45, -16],
    ['clamp(88px, 21vw, 184px)', 'clamp(40px, 9vw, 78px)', -8, 31, -5],
    ['clamp(126px, 30vw, 300px)', 'clamp(56px, 13.5vw, 132px)', 18, 53, -24],
    ['clamp(104px, 25vw, 238px)', 'clamp(47px, 10.5vw, 96px)', -62, 41, -12],
    ['clamp(80px, 18vw, 152px)', 'clamp(36px, 8vw, 66px)', 72, 29, -19],
    ['clamp(114px, 27vw, 276px)', 'clamp(51px, 12vw, 108px)', 31, 49, -29],
    ['clamp(95px, 22vw, 205px)', 'clamp(43px, 9.5vw, 82px)', -41, 35, -14]
  ];
  projects.forEach((path, index) => {
    const [rx, ry, tilt, duration, delay] = orbits[index];
    const orbit = el('div', 'home-project-orbit');
    orbit.style.setProperty('--orbit-rx', rx);
    orbit.style.setProperty('--orbit-ry', ry);
    orbit.style.setProperty('--orbit-tilt', `${tilt}deg`);
    orbit.append(el('span', 'home-orbit-track'));
    const dot = button('', 'home-project-dot', guard(() => openProject(path)), `${projectName(path)} 작업 공간 열기`);
    dot.style.setProperty('--orbit-duration', `${duration}s`);
    dot.style.setProperty('--orbit-delay', `${delay}s`);
    dot.style.setProperty('--orbit-rest-distance', `${[25, 58, 82][index % 3]}%`);
    dot.style.setProperty('--dot-color', ['#f9eaff', '#d5aaff', '#a9c9ff', '#ffc1e5', '#c4ffd7', '#ffe6a8'][index % 6]);
    dot.dataset.workspace = path;
    dot.append(el('span', 'home-project-dot-mark'));
    const label = button(projectName(path), 'home-project-label', guard(() => openProject(path)), `${projectName(path)} 작업 공간 열기`);
    label.style.setProperty('--orbit-rx', rx);
    label.style.setProperty('--orbit-ry', ry);
    label.style.setProperty('--orbit-duration', `${duration}s`);
    label.style.setProperty('--orbit-delay', `${delay}s`);
    label.style.setProperty('--orbit-rest-distance', `${[25, 58, 82][index % 3]}%`);
    label.dataset.workspace = path;
    let hoverTimer;
    const setLabelActive = active => {
      clearTimeout(hoverTimer);
      if (active) {
        dot.classList.add('hover-active');
        label.classList.add('active');
      } else {
        hoverTimer = setTimeout(() => {
          dot.classList.remove('hover-active');
          label.classList.remove('active');
        }, 60);
      }
    };
    dot.addEventListener('mouseenter', () => setLabelActive(true));
    dot.addEventListener('mouseleave', () => setLabelActive(false));
    dot.addEventListener('focus', () => setLabelActive(true));
    dot.addEventListener('blur', () => setLabelActive(false));
    label.addEventListener('mouseenter', () => setLabelActive(true));
    label.addEventListener('mouseleave', () => setLabelActive(false));
    label.addEventListener('focus', () => setLabelActive(true));
    label.addEventListener('blur', () => setLabelActive(false));
    const openMenu = event => { event.preventDefault(); event.stopPropagation(); showProjectMenu(path, event.currentTarget); };
    const openMenuKey = event => { if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) openMenu(event); };
    dot.addEventListener('contextmenu', openMenu);
    label.addEventListener('contextmenu', openMenu);
    dot.addEventListener('keydown', openMenuKey);
    label.addEventListener('keydown', openMenuKey);
    labelLayer.append(label);
    orbit.append(dot);
    system.append(orbit);
  });
}
async function refreshSavedSessions() {
  if (!isDesktop) return;
  const epoch=++state.savedEpoch;
  const result = await call('savedSessions', { cwd: state.folder, allWorkspaces: false });
  if(epoch!==state.savedEpoch) return;
  state.savedSessions = Array.isArray(result.sessions) ? result.sessions : [];
  renderSavedSessions();
}
function liveSavedSessions() {
  return state.terminals.filter(t => !t.pane.exited && !t.resumeId && (t.profile === 'claude' || t.profile === 'codex') && matchesProject(t.pane.cwd, state.folder)
    && !state.savedSessions.some(s => s.provider === t.profile && matchesProject(s.cwd, t.pane.cwd) && new Date(s.updated).getTime() >= t.createdAt))
    .map(t => ({ provider: t.profile, title: t.name, cwd: t.pane.cwd, live: true, terminalId: t.id }));
}
function activateLiveSession(item) { const t = state.terminals.find(x => x.id === item.terminalId); if (t) { activateTerminal(t.id); t.pane.focus(); } }
function renderSavedSessions() {
  savedList.replaceChildren(); const q=savedSearch.value.trim().toLowerCase();
  const rows=[...liveSavedSessions(), ...state.savedSessions].filter(s=>`${s.title} ${s.cwd} ${s.provider}`.toLowerCase().includes(q));
  if(!rows.length) return savedList.append(el('p','tree-hint','저장된 대화가 없습니다.'));
  for(const item of rows){const row=el('div',`saved-session ${item.provider}`);row.title=item.cwd;const resume=button('', 'saved-session-open',guard(()=>item.live?activateLiveSession(item):resumeSavedSession(item)));const time=item.live?'진행 중':new Date(item.updated).toLocaleDateString('ko-KR',{month:'numeric',day:'numeric'});resume.append(el('strong','',item.title),el('small','',`${item.provider==='codex'?'Codex':'Claude'} · ${basename(item.cwd)} · ${time}`));row.append(resume);if(!item.live){const remove=button('','saved-session-delete',guard(()=>deleteSavedSession(item)),`${item.title} 영구 삭제`);remove.append(icon('close'));remove.setAttribute('aria-label',`${item.title} 영구 삭제`);row.append(remove);}savedList.append(row);}
}
async function deleteSavedSession(item) {
  if(!await confirm('대화 영구 삭제',`“${item.title}” 대화를 영구 삭제합니다. 이 작업은 되돌릴 수 없습니다.`,'영구 삭제')) return false;
  const result=await call('deleteSavedSession',{provider:item.provider,id:item.id,cwd:state.folder,allWorkspaces:false});
  state.savedSessions=Array.isArray(result.sessions)?result.sessions:[];renderSavedSessions();toast('대화를 영구 삭제했습니다.');return true;
}
async function openResumePicker(provider) {
  if(!state.savedSessions.length) await refreshSavedSessions(); const rows=state.savedSessions.filter(s=>s.provider===provider);
  dialog(`${profiles[provider].name} 대화`,node=>{node.append(el('p','dialog-note','원래 작업 폴더에서 이어서 엽니다.'));for(const item of rows)node.append(button(item.title,'command-item',guard(async()=>{$('#modal').close();await resumeSavedSession(item);})));node.append(button(`새 ${profiles[provider].name} 세션`,'secondary',guard(async()=>{$('#modal').close();await newTerminal(provider);})));});
}
async function resumeSavedSession(item) {const open=state.terminals.find(t=>t.profile===item.provider&&t.resumeId===item.id&&!t.pane.exited);if(open){activateTerminal(open.id);open.pane.focus();return;}await newTerminal(item.provider,{resumeId:item.id,cwd:item.cwd,title:item.title});}
function persist() { return call('settings', state.settings).catch(e => toast(e.message, true)); }
function matchesProject(left, right) { return typeof left === 'string' && typeof right === 'string' && left.toLowerCase() === right.toLowerCase(); }
function visibleProjects() {
  if (state.client) return state.client.projects.map(item => item.path).slice(0, 8);
  const removed = state.settings.removedProjects || [];
  return [state.folder, ...state.settings.recent]
    .filter((path, index, paths) => typeof path === 'string' && path && !removed.some(value => matchesProject(value, path)) && paths.findIndex(value => matchesProject(value, path)) === index)
    .slice(0, 8);
}
async function removeProject(path) {
  const name = basename(path);
  const accepted = await confirm('최근 프로젝트에서 제거', `“${name}”을(를) Orbit 홈에서 제거할까요? 프로젝트 폴더와 파일은 삭제되지 않습니다.`, '홈에서 제거');
  if (!accepted) return false;
  state.settings.recent = state.settings.recent.filter(value => !matchesProject(value, path));
  state.settings.removedProjects = [path, ...state.settings.removedProjects.filter(value => !matchesProject(value, path))].slice(0, 16);
  const wasCurrent = matchesProject(state.folder, path);
  if (wasCurrent) state.folder = '';
  // Update first so a successful confirmation removes the node immediately;
  // settings is the only persisted data that is changed.
  renderHomeOrbit();
  if (wasCurrent) showHome();
  await persist();
  toast(`“${name}”을(를) 최근 프로젝트에서 제거했습니다.`);
  return true;
}
let projectMenu;
function setProjectMenuPaused(path, paused) {
  const selector = `[data-workspace="${CSS.escape(path)}"]`;
  document.querySelectorAll(`.home-project-dot${selector},.home-project-label${selector}`).forEach(node => node.classList.toggle('menu-open', paused));
}
function closeProjectMenu() {
  if (!projectMenu) return;
  setProjectMenuPaused(projectMenu.dataset.workspace, false);
  projectMenu.remove(); projectMenu = null;
  document.removeEventListener('pointerdown', closeProjectMenuOnOutsidePointer, true);
  document.removeEventListener('keydown', closeProjectMenuOnEscape, true);
}
function closeProjectMenuOnOutsidePointer(event) { if (projectMenu && !projectMenu.contains(event.target)) closeProjectMenu(); }
function closeProjectMenuOnEscape(event) { if (event.key === 'Escape') closeProjectMenu(); }
function showProjectMenu(path, anchor) {
  closeProjectMenu();
  const system = $('#home-orbit'); if (!system) return;
  const menu = el('div', 'home-project-menu'); menu.setAttribute('role', 'menu'); menu.setAttribute('aria-label', `${projectName(path)} 프로젝트 메뉴`);
  menu.dataset.workspace = path;
  setProjectMenuPaused(path, true);
  const action = (label, run) => { const item = button(label, 'home-project-menu-item', guard(async () => { closeProjectMenu(); await run(); })); item.setAttribute('role', 'menuitem'); menu.append(item); };
  action('작업 공간 열기', () => setFolder(path));
  action('프로젝트 이름 편집', () => editProjectName(path));
  action('프로젝트 제거', () => removeProject(path));
  menu.addEventListener('pointerdown', event => event.stopPropagation());
  menu.addEventListener('keydown', event => {
    const items = [...menu.querySelectorAll('button')], current = items.indexOf(document.activeElement);
    let next = null;
    if (event.key === 'ArrowDown') next = (current + 1 + items.length) % items.length;
    if (event.key === 'ArrowUp') next = (current - 1 + items.length) % items.length;
    if (event.key === 'Home') next = 0;
    if (event.key === 'End') next = items.length - 1;
    if (next !== null) { event.preventDefault(); items[next]?.focus(); }
  });
  system.append(menu); projectMenu = menu;
  requestAnimationFrame(() => {
    if (projectMenu !== menu) return;
    const bounds = system.getBoundingClientRect(), target = anchor.getBoundingClientRect();
    const left = Math.max(8, Math.min(bounds.width - menu.offsetWidth - 8, target.left - bounds.left + target.width / 2 - menu.offsetWidth / 2));
    let top = target.top - bounds.top - menu.offsetHeight - 8;
    if (top < 8) top = Math.min(bounds.height - menu.offsetHeight - 8, target.bottom - bounds.top + 8);
    menu.style.left = `${left}px`; menu.style.top = `${top}px`; menu.querySelector('button')?.focus();
    document.addEventListener('pointerdown', closeProjectMenuOnOutsidePointer, true);
    document.addEventListener('keydown', closeProjectMenuOnEscape, true);
  });
}
async function editProjectName(path) {
  const input = el('input', 'dialog-input'); input.value = state.settings.projectNames[projectKey(path)] || '';
  const answer = await dialog('프로젝트 이름 편집', node => { node.append(el('p', 'dialog-note', '이 이름은 Orbit에만 표시되며 프로젝트 폴더나 경로는 바뀌지 않습니다.'), input); }, [{ value: 'cancel', label: '취소' }, { value: 'save', label: '저장', primary: true }]);
  if (answer !== 'save') return false;
  const name = input.value.trim().slice(0, 48), key = projectKey(path);
  if (name) state.settings.projectNames[key] = name; else delete state.settings.projectNames[key];
  renderHomeOrbit(); await persist(); return true;
}
async function chooseLocal(mode) {
  const { openFilePicker } = await import('./file-picker.js');
  return openFilePicker({ mode, cwd: state.folder, workspace: state.folder, recent: state.settings.recent });
}
async function pickFolder() { const path = await chooseLocal('folder'); if (path) await setFolder(path); }
function addRecentProject(path) {
  const matches = value => matchesProject(value, path);
  state.settings.recent = [path, ...state.settings.recent.filter(value => typeof value === 'string' && !matches(value))].slice(0, 8);
  state.settings.removedProjects = state.settings.removedProjects.filter(value => !matches(value));
  persist();
  renderHomeOrbit();
}
async function pickHomeProject() {
  // The Home picker registers a project only. Its orbit node remains the action
  // that enters the workspace, unlike the workspace folder picker above.
  const path = await chooseLocal('folder');
  if (path) addRecentProject(path);
}
async function refreshTree() { $('#file-filter').value = ''; const root = $('#file-tree'); root.replaceChildren(el('p','tree-hint','불러오는 중…')); await loadTree(state.folder, root, 0); }
async function loadTree(path, parent, depth) {
  const { entries, truncated } = await call('list', { path, hidden: state.hidden }); parent.replaceChildren();
  if (!entries.length) parent.append(el('p','tree-hint','비어 있는 폴더'));
  for (const entry of entries) {
    const row = button('', `tree-row ${entry.directory ? 'tree-directory' : 'tree-file'}`, guard(async () => {
      if (entry.directory) { const child = row.nextElementSibling; if (child?.classList.contains('tree-children')) { child.remove(); row.classList.remove('expanded'); } else { const nested = el('div','tree-children'); row.after(nested); row.classList.add('expanded'); await loadTree(entry.path,nested,depth+1); } }
      else await openFile(entry.path);
    }), entry.path);
    row.dataset.name = entry.name.toLowerCase(); row.style.paddingLeft = `${15 + depth * 14}px`;
    row.dataset.path = entry.path;
    if(!entry.directory && state.file?.path.toLowerCase()===entry.path.toLowerCase())row.classList.add('selected');
    if(entry.directory){const disclosure=icon('chevronRight');disclosure.classList.add('folder-glyph');row.append(disclosure,icon('folder'));}else row.append(el('span',`file-glyph ${fileColor(entry.name)}`,fileGlyph(entry.name)));row.append(el('span','tree-name',entry.name)); parent.append(row);
  }
  if (truncated) parent.append(el('p','tree-hint','처음 1,500개 항목만 표시합니다.'));
}
function fileGlyph(name) { const ext = name.split('.').at(-1); return ({ md: 'M↓', json: '{}', js: 'JS', ts: 'TS', css: '#', html: '◇', py: 'Py', cs: 'C#', ps1: '>_' })[ext] || '≡'; }
function fileColor(name) { return /\.(js|json|yaml|yml)$/.test(name) ? 'gold' : /\.(ts|css|py)$/.test(name) ? 'blue' : /\.(md|cs)$/.test(name) ? 'purple' : ''; }
async function newFile() { const path = await chooseLocal('create'); if (path) { await refreshTree(); await openFile(path); } }
async function openFilePicker() { const path = await chooseLocal('file'); if (path) await openFile(path); }

async function newTerminal(profile = state.settings.defaultShell, resume = null) {
  if (!isDesktop) { await call('createTerminal'); return null; }
  if (state.terminals.filter(t => !t.pane.exited).length + state.terminalCreating >= 8) throw new Error('동시에 8개까지 터미널을 열 수 있습니다.');
  state.terminalCreating++;
  try {
  const { TerminalPane } = await import('./terminal.js');
  const id = crypto.randomUUID(), p = profiles[profile];
  const cwd = resume?.cwd || state.folder;
  const name = resume?.title || `${p.name} ${state.terminals.filter(t => t.profile === profile).length + 1}`;
  const pane = new TerminalPane({ id, profile, cwd, resumeId: resume?.resumeId, name, settings: state.settings, openLink: guard(openLink), onExit: renderTerminals, onFocus: () => activateTerminal(id) });
  const session = { id, profile, resumeId: resume?.resumeId || '', name, pane, createdAt: Date.now() };
  state.terminals.push(session); addToGroup(activeGroup(),id); state.active = id; renderTerminals();
  try { await pane.start(); renderTerminals(); if (profile === 'claude' || profile === 'codex') refreshSavedSessions().catch(() => {}); return { session: id, pid: pane.pid }; }
  catch (e) { removeTab(id); pane.dispose(); state.terminals = state.terminals.filter(t => t.id !== id); state.active = state.terminals.at(-1)?.id; renderTerminals(); throw e; }
  } finally { state.terminalCreating--; }
}
function activeTerminal() { return state.terminals.find(t => t.id === state.active); }
function leafs(node=state.terminalRoot,result=[]) { if(node.type==='group') result.push(node); else { leafs(node.first,result);leafs(node.second,result); } return result; }
function activeGroup() { return leafs().find(g=>g.id===state.activeGroup) || leafs()[0]; }
function addToGroup(group,id,index=group.tabs.length) { group.tabs.splice(index,0,id);group.active=id;state.activeGroup=group.id; }
function findGroup(id) { return leafs().find(g=>g.tabs.includes(id)); }
function activateTerminal(id) { const group=findGroup(id);if(!group)return;if(state.active===id && state.activeGroup===group.id)return;group.active=id;state.active=id;state.activeGroup=group.id;const node=document.querySelector(`.terminal-group[data-group-id="${group.id}"]`),session=state.terminals.find(t=>t.id===id);if(node&&session){node.querySelectorAll('.terminal-tab').forEach(tab=>{const active=tab.dataset.terminalId===id;tab.classList.toggle('active',active);tab.setAttribute('aria-selected',String(active));});const body=node.querySelector('.terminal-group-body');body.replaceChildren(session.pane.element);session.pane.element.hidden=false;requestAnimationFrame(()=>session.pane.focus());}else renderTerminals(); }
function replaceNode(node,oldNode,next) { if(node===oldNode) return next; if(node.type==='split') { node.first=replaceNode(node.first,oldNode,next);node.second=replaceNode(node.second,oldNode,next); } return node; }
function compact(node) { if(node.type==='group')return node;node.first=compact(node.first);node.second=compact(node.second);if(node.first.type==='group'&&!node.first.tabs.length)return node.second;if(node.second.type==='group'&&!node.second.tabs.length)return node.first;return node; }
function removeTab(id) { const group=findGroup(id);if(!group)return;group.tabs=group.tabs.filter(x=>x!==id);if(group.active===id)group.active=group.tabs.at(-1)||null;state.terminalRoot=compact(state.terminalRoot);const next=activeGroup();state.activeGroup=next.id;state.active=next.active||next.tabs[0]||null; }
function splitGroup(group,id,side) { group.tabs=group.tabs.filter(x=>x!==id);if(group.active===id)group.active=group.tabs[0]||null;const fresh={type:'group',id:crypto.randomUUID(),tabs:[id],active:id};const first=(side==='left'||side==='top')?fresh:group,second=first===fresh?group:fresh;const split={type:'split',id:crypto.randomUUID(),direction:(side==='top'||side==='bottom')?'column':'row',ratio:.5,first,second};state.terminalRoot=replaceNode(state.terminalRoot,group,split);state.activeGroup=fresh.id;state.active=id; }
function moveTab(id,target,edge,index) { const from=findGroup(id);if(!from)return;if(edge && edge!=='center') { if(from!==target){from.tabs=from.tabs.filter(x=>x!==id);if(from.active===id)from.active=from.tabs[0]||null;}splitGroup(target,id,edge);state.terminalRoot=compact(state.terminalRoot); } else { const old=from.tabs.indexOf(id);from.tabs=from.tabs.filter(x=>x!==id);if(from.active===id)from.active=from.tabs[0]||null;if(from===target && old>=0 && index>old)index--;addToGroup(target,id,Number.isInteger(index)?index:target.tabs.length);state.active=id;state.terminalRoot=compact(state.terminalRoot); }renderTerminals(); }
function renderTerminalTab(group,t) { let tab=t.tabElement;if(!tab){tab=el('div','terminal-tab');tab.draggable=true;tab.dataset.terminalId=t.id;tab.setAttribute('role','tab');tab.ondragstart=e=>{e.dataTransfer.setData('text/orbit-terminal',t.id);e.dataTransfer.effectAllowed='move';};const b=button('', 'terminal-tab-name',()=>activateTerminal(t.id));b.append(el('span','tab-indicator'),el('span','',t.name));b.ondblclick=()=>renameTerminal(t);const close=button('','tab-close',guard(()=>closeTerminal(t)),'터미널 종료');close.append(icon('close'));tab.append(b,close);t.tabElement=tab;}const name=tab.querySelector('.terminal-tab-name'),indicator=tab.querySelector('.tab-indicator'),close=tab.querySelector('.tab-close');name.title=`${t.pane.cwd}${t.pane.pid?` · PID ${t.pane.pid}`:''}`;name.lastElementChild.textContent=t.name;indicator.classList.toggle('ended',t.pane.exited);close.setAttribute('aria-label',`${t.name} 종료`);tab.dataset.groupId=group.id;tab.classList.toggle('active',t.id===group.active);tab.setAttribute('aria-selected',String(t.id===group.active));return tab; }
function renderGroup(group) { const node=el('section','terminal-group');node.dataset.groupId=group.id;node.tabIndex=0;node.setAttribute('role','region');node.setAttribute('aria-label','터미널 그룹');const tabs=el('div','terminal-tabs terminal-group-tabs');tabs.setAttribute('role','tablist');for(const id of group.tabs){const t=state.terminals.find(x=>x.id===id);if(t)tabs.append(renderTerminalTab(group,t));}const add=button('','tab-add',()=>launchDialog(group.id),'새 터미널');add.append(icon('plus'));tabs.append(add);const content=el('div','terminal-group-body');const active=state.terminals.find(t=>t.id===group.active)||state.terminals.find(t=>group.tabs.includes(t.id));if(active){group.active=active.id;active.pane.element.hidden=false;active.pane.element.classList.toggle('focused',state.active===active.id);content.append(active.pane.element);}node.append(tabs,content);setupDrop(tabs,group,true);setupDrop(content,group);return node; }
function renderNode(tree) { if(tree.type==='group')return renderGroup(tree);const node=el('div',`terminal-split ${tree.direction}`);node.dataset.splitId=tree.id;node.style.setProperty('--split-first',`${tree.ratio*100}%`);node.append(renderNode(tree.first),makeSplitter(tree),renderNode(tree.second));return node; }
function setupDrop(node,group,forceCenter=false) { node.ondragover=e=>{if(!e.dataTransfer.types.includes('text/orbit-terminal'))return;e.preventDefault();const r=node.getBoundingClientRect(),x=(e.clientX-r.left)/r.width,y=(e.clientY-r.top)/r.height;const edge=forceCenter?'':x<.22?'left':x>.78?'right':y<.22?'top':y>.78?'bottom':'';node.dataset.drop=edge||'center';e.dataTransfer.dropEffect='move';};node.ondragleave=()=>delete node.dataset.drop;node.ondrop=e=>{e.preventDefault();const id=e.dataTransfer.getData('text/orbit-terminal'),edge=node.dataset.drop;delete node.dataset.drop;if(!id)return;let index;if(forceCenter)index=[...node.querySelectorAll('.terminal-tab')].findIndex(tab=>e.clientX<tab.getBoundingClientRect().left+tab.getBoundingClientRect().width/2);moveTab(id,group,edge,index<0?group.tabs.length:index);}; }
function makeSplitter(tree) { const splitter=el('div',`terminal-splitter ${tree.direction}`);splitter.dataset.splitId=tree.id;splitter.tabIndex=0;splitter.setAttribute('role','separator');splitter.setAttribute('aria-orientation',tree.direction==='row'?'vertical':'horizontal');const adjust=delta=>{tree.ratio=Math.max(.18,Math.min(.82,tree.ratio+delta));renderTerminals();};splitter.onpointerdown=e=>{splitter.setPointerCapture(e.pointerId);const start=tree.ratio,box=splitter.parentElement.getBoundingClientRect(),axis=tree.direction==='row'?box.width:box.height,origin=tree.direction==='row'?e.clientX:e.clientY;const move=m=>{tree.ratio=Math.max(.18,Math.min(.82,start+((tree.direction==='row'?m.clientX:m.clientY)-origin)/axis));splitter.parentElement.style.setProperty('--split-first',`${tree.ratio*100}%`);};splitter.onpointermove=move;splitter.onpointerup=()=>{splitter.onpointermove=null;renderTerminals();};};splitter.onkeydown=e=>{if(['ArrowLeft','ArrowUp'].includes(e.key)){e.preventDefault();adjust(-.04);}if(['ArrowRight','ArrowDown'].includes(e.key)){e.preventDefault();adjust(.04);}};return splitter; }
function renderTerminals() { const has=state.terminals.length>0,layout=$('#terminal-layout');$('#welcome').hidden=has;layout.hidden=!has;layout.replaceChildren();if(has)layout.append(renderNode(state.terminalRoot));$('#session-count').textContent=state.terminals.length;$('#status-sessions').textContent=`${state.terminals.filter(t=>!t.pane.exited).length}개 실행 중`;requestAnimationFrame(()=>{const t=activeTerminal();if(t)t.pane.focus();});renderSavedSessions(); }
async function closeTerminal(t) {
  if (!t.pane.exited && !await confirm('터미널 종료', `${t.name}과 연결된 에이전트 및 하위 프로세스를 종료합니다.`, '종료')) return;
  await call('closeTerminal', { session: t.id });removeTab(t.id);t.pane.dispose();state.terminals = state.terminals.filter(x => x !== t);renderTerminals();
}
async function renameTerminal(t) { const input = el('input','dialog-input');input.value = t.name; const answer = await dialog('터미널 이름', node => node.append(input), [{value:'cancel',label:'취소'},{value:'save',label:'변경',primary:true}]);if(answer==='save' && input.value.trim()){t.name=input.value.trim().slice(0,40);t.pane.name=t.name;notify('terminalName',{session:t.id,name:t.name});renderTerminals();} }
async function toggleSplit() { const group=activeGroup(),id=group.active;if(!id||group.tabs.length<2){toast('같은 그룹에 터미널 탭이 2개 이상 있어야 분할할 수 있습니다.');return;}splitGroup(group,id,'right');renderTerminals(); }
function launchDialog(groupId) {
  if(groupId)state.activeGroup=groupId;
  dialog('새 터미널', node => {
    node.append(el('p','muted',`작업 폴더 · ${state.folder}`));const choices = el('div','launch-choices');
    for(const [key,p] of Object.entries(profiles)){const b=button('', 'launch-choice', guard(async()=>{$('#modal').close();await newTerminal(key);}));b.append(el('span',`choice-mark ${p.class}`,p.mark),el('strong','',p.name),el('small','muted',p.subtitle));choices.append(b);}node.append(choices);
    node.append(el('p','dialog-note','CLI는 PC에 설치되어 있고 PATH에서 실행 가능해야 합니다. 로그인과 권한 요청은 각 터미널에서 진행합니다.'));
  });
}
function terminalSearch() {
  const t=activeTerminal();if(!t){toast('먼저 터미널을 열어 주세요.');return;}
  dialog('터미널에서 찾기',node=>{const input=el('input','dialog-input');input.placeholder='검색어';const count=el('p','muted','입력 후 Enter로 다음 결과를 찾습니다.');const find=()=>{const found=t.pane.search.findNext(input.value,{incremental:true});count.textContent=found?'일치하는 결과를 표시했습니다.':'일치하는 결과가 없습니다.';};input.addEventListener('input',find);input.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();t.pane.search.findNext(input.value);}});node.append(input,count);});
}

function syncFileState() { if(state.editor && state.file)state.file.state=state.editor.state; }
const isMarkdown=file=>/\.(md|markdown|mdown|mkdn)$/i.test(file?.name||'');
async function setMarkdownMode(mode) {
  const file=state.file;if(!file || !isMarkdown(file))return;
  const epoch=++state.markdownEpoch;
  file.markdownMode=mode;const preview=$('#markdown-preview'),host=$('#editor-host');
  if(mode==='preview'){
    const module=state.markdownModule ||= await import('./markdown.js');
    if(state.file!==file || file.markdownMode!==mode || epoch!==state.markdownEpoch)return;
    preview.innerHTML=module.renderMarkdown(state.editor?.state.sliceDoc() ?? file.content);
    host.hidden=true;preview.hidden=false;
  } else { preview.hidden=true;host.hidden=false;state.editor?.focus(); }
  document.querySelectorAll('[data-editor-mode]').forEach(button=>button.hidden=!isMarkdown(file) || button.dataset.editorMode===mode);
}
$('#markdown-preview').addEventListener('click',event=>{const copy=event.target.closest('[data-md-copy]'),link=event.target.closest('[data-md-link]');if(link)event.preventDefault();guard(async()=>{if(copy){await call('clipboard',{text:decodeURIComponent(copy.dataset.mdCopy)});toast('코드 블록을 복사했습니다.');return;}if(link)await openLink(link.dataset.mdLink,state.file?.path.replace(/[\\/][^\\/]+$/,'')||state.folder);})();});
let editorOperationTail = Promise.resolve();
function editorOperation(action) {
  const operation = editorOperationTail.then(action);
  editorOperationTail = operation.catch(() => {});
  return operation;
}
function openFile(path,line=1,column=1) { return editorOperation(() => openFileNow(path,line,column)); }
async function openFileNow(path,line,column) {
  state.loadingFile=true;
  try {
    let file=state.files.find(f=>f.path.toLowerCase()===path.toLowerCase());
    if(!file){
      if(state.files.length>=8)throw new Error('파일 탭은 8개까지 열 수 있습니다. 사용하지 않는 파일을 닫아 주세요.');
      try {file=await call('read',{path,cwd:state.folder});}
      catch(e){ const answer=await dialog('파일 열기',node=>node.append(el('p','muted',e.message)),[{value:'cancel',label:'닫기'},{value:'external',label:'기본 앱에서 열기',primary:true}]);if(answer==='external')await call('external',{path,cwd:state.folder});return; }
      file.dirty=false;state.files.push(file);
    }
    await activateFileNow(file,line,column);
  }finally{state.loadingFile=false;}
}
function activateFile(file,line,column) { return editorOperation(() => activateFileNow(file,line,column)); }
async function activateFileNow(file,line,column) {
  if(!state.files.includes(file))return;
  state.markdownEpoch++;
  syncFileState();state.editor?.destroy();state.editor=null;state.file=file;$('#editor-section').hidden=false;$('#work-area').classList.add('has-editor');
  const module=state.editorModule ||= await import('./editor.js');
  state.editor=await module.createEditor($('#editor-host'),file,()=>{file.dirty=true;renderFileTabs();notifyDirty();},guard(saveFile),(ln,col)=>$('#editor-cursor').textContent=`Ln ${ln}, Col ${col}`,state.settings.theme);
  if(file.state)state.editor.setState(file.state);
  module.updateTheme(state.editor,state.settings.theme);
  $('#editor-path').textContent=file.path;$('#editor-path').title=file.path;$('#editor-encoding').textContent=`${file.encoding} · ${file.newline}`;$('#editor-language').textContent=file.name.split('.').at(-1).toUpperCase();
  document.querySelectorAll('.tree-file').forEach(row=>row.classList.toggle('selected',row.dataset.path?.toLowerCase()===file.path.toLowerCase()));
  if(isMarkdown(file))await setMarkdownMode(file.markdownMode||'preview');else {$('#markdown-preview').hidden=true;$('#editor-host').hidden=false;document.querySelectorAll('[data-editor-mode]').forEach(button=>button.hidden=true);}
  renderFileTabs();if(!isMarkdown(file) || file.markdownMode==='edit'){if(line)module.goTo(state.editor,line,column);else state.editor.focus();}
}
function renderFileTabs() {
  const strip=$('#editor-tabs');strip.replaceChildren();
  for(const file of state.files){const tab=el('div',`editor-tab ${file===state.file?'active':''}`);const b=button(`${file.dirty?'● ':''}${file.name}`,'editor-tab-name',guard(()=>activateFile(file)));b.title=file.path;const close=button('','tab-close',guard(()=>closeFile(file)));close.append(icon('close'));close.setAttribute('aria-label',`${file.name} 닫기`);tab.append(b,close);strip.append(tab);}
}
function notifyDirty(){notify('dirty',{value:state.files.some(f=>f.dirty)});}
async function saveFile(){
  const file=state.file,view=state.editor;if(!file || !view)return;
  if(file.saving)return file.saving;
  const content=view.state.sliceDoc();
  file.saving=(async()=>{
    const result=await call('save',{path:file.path,content,encoding:file.encoding,revision:file.revision});
    file.revision=result.revision;file.content=content;
    const latest=file===state.file ? (state.editor?.state || file.state) : file.state;
    file.dirty=(latest?.sliceDoc() ?? content)!==content;
    renderFileTabs();notifyDirty();toast(`${file.name} 저장 완료`);
  })().finally(()=>{file.saving=null;});
  return file.saving;
}
function closeFile(file){return editorOperation(async()=>{
  if(!state.files.includes(file))return;
  if(file.saving)await file.saving;
  if(file.dirty && !await confirm('저장하지 않은 변경 사항',`${file.name}의 변경 사항을 버리고 닫을까요?`,'변경 사항 버리기'))return;
  state.files=state.files.filter(f=>f!==file);
  if(state.file===file){state.editor?.destroy();state.editor=null;state.file=null;if(state.files.length)await activateFileNow(state.files.at(-1));else{$('#editor-section').hidden=true;$('#work-area').classList.remove('has-editor');}}
  renderFileTabs();notifyDirty();
});}
function reloadFile(){
  const file=state.file;
  return editorOperation(async()=>{
    if(!file || !state.files.includes(file))return;
    if(file.saving)await file.saving;
    if(file.dirty && !await confirm('파일 다시 열기','저장하지 않은 변경을 버리고 디스크의 최신 내용을 읽습니다.','다시 열기'))return;
    const fresh=await call('read',{path:file.path});
    syncFileState();state.editor?.destroy();state.editor=null;
    Object.assign(file,fresh,{dirty:false,state:null});await activateFileNow(file);notifyDirty();
  });
}
const externalFileExtensions=new Set(['.exe','.lnk','.pdf','.png','.jpg','.jpeg','.gif','.webp','.bmp','.ico','.svg','.doc','.docx','.xls','.xlsx','.ppt','.pptx','.zip','.7z','.rar']);
const opensInDefaultApp=path=>externalFileExtensions.has((path.match(/\.[^.\\/]+$/)?.[0]||'').toLowerCase());
async function openLink(value,cwd=state.folder){
  if(value.startsWith('#')){let fragment=value.slice(1);try{fragment=decodeURIComponent(fragment);}catch{}$('#markdown-preview').querySelector(`#${CSS.escape(fragment)}`)?.scrollIntoView({block:'start'});return;}
  const local=parseLocation(value);
  if(local.kind==='url'){await call('external',{path:local.path});return;}
  const info=await call('stat',{path:local.path,cwd});if(!info.exists)throw new Error(`경로를 찾을 수 없습니다: ${local.path} (터미널 시작 폴더 기준)`);
  if(info.directory||opensInDefaultApp(info.path))await call('external',{path:info.path});else await openFile(info.path,local.line,local.column);
}
async function pasteActive(text){const t=activeTerminal();if(!t)throw new Error('먼저 입력할 터미널을 선택해 주세요.');await t.pane.paste(text);}
async function pasteClipboardToActive(){const t=activeTerminal();if(!t)throw new Error('먼저 입력할 터미널을 선택해 주세요.');await t.pane.pasteClipboard();}

const defaultPrompts=[{title:'변경 사항 리뷰',text:'현재 변경 사항을 리뷰해 줘. 버그, 누락된 예외 처리, 기존 동작에 영향을 줄 수 있는 부분을 우선 확인해 줘.'},{title:'작업 이어가기',text:'이 프로젝트의 AGENTS.md와 README를 읽고 현재 상태를 확인한 뒤, 다음 작업을 진행해 줘.'},{title:'테스트와 결과 정리',text:'이번 변경에 필요한 테스트를 실행하고, 수정한 내용과 검증 결과, 남아 있는 문제를 간단히 정리해 줘.'}];
function promptLibrary(){
  const prompts=state.settings.prompts || defaultPrompts;
  dialog('프롬프트 보관함',node=>{
    node.append(el('p','muted','자주 쓰는 요청을 편집하고, 선택한 에이전트 터미널에 붙여넣으세요.'));
    const select=el('select','dialog-input');prompts.forEach((p,i)=>{const opt=el('option','',p.title);opt.value=i;select.append(opt);});
    const title=el('input','dialog-input');title.placeholder='프롬프트 이름';title.value=prompts[0].title;
    const text=el('textarea','prompt-text');text.value=prompts[0].text;select.onchange=()=>{title.value=prompts[+select.value].title;text.value=prompts[+select.value].text;};
    const actions=el('div','prompt-actions');actions.append(button('보관함에 저장','secondary',()=>{prompts[+select.value]={title:title.value||'프롬프트',text:text.value};state.settings.prompts=prompts;persist();select.options[+select.value].textContent=title.value;toast('프롬프트를 저장했습니다.');}),button('터미널에 붙여넣기 ↗','primary',guard(async()=>{await pasteActive(text.value);$('#modal').close();})));
    node.append(select,title,text,actions,el('p','dialog-note','에이전트가 입력을 기다리는 터미널을 선택해 주세요. 여러 줄 입력은 셸에서 바로 실행될 수 있습니다.'));
  });
}
function recentFolders(){dialog('최근 작업 폴더',node=>{for(const path of state.settings.recent)node.append(button(path,'recent-folder',guard(async()=>{$('#modal').close();await setFolder(path);})));});}
function updatePower(){$('#power-label').textContent=state.settings.lowPower?'가볍게 실행 중':'넉넉한 기록 모드';$('#power-detail').textContent=`${state.settings.lowPower?'절약 모드':'일반 모드'} · 스크롤 기록 ${state.settings.lowPower?'500':'2,000'}줄`;}
function applyTheme() {
  document.documentElement.dataset.theme = state.settings.theme;
  notify('theme', { value: state.settings.theme });
  for (const terminal of state.terminals) terminal.pane.configure(state.settings);
  if (state.editor && state.editorModule) state.editorModule.updateTheme(state.editor, state.settings.theme);
}
function checkForUpdate(){
  call('updateCheck').then(result=>{
    if(!result||!result.available)return;
    for(const badge of document.querySelectorAll('[data-update]')){
      badge.textContent='업데이트 v'+result.version;badge.title='현재 v'+result.current+' → 새 버전 v'+result.version;badge.hidden=false;
      badge.onclick=guard(()=>installUpdate(result));
    }
  }).catch(()=>{});
}
async function installUpdate(result){
  const running=state.terminals.length;
  const text='v'+result.current+'에서 v'+result.version+'으로 업데이트합니다. Orbit이 종료된 뒤 새 버전이 설치되고 자동으로 다시 실행됩니다.'+(running?' 실행 중인 터미널 '+running+'개가 종료됩니다.':'')+' 저장하지 않은 파일이 있으면 먼저 저장하세요.';
  if(!await confirm('업데이트 v'+result.version,text,'지금 업데이트'))return;
  toast('업데이트를 내려받는 중입니다…');
  await call('updateInstall');
}
const DEFAULT_ACCOUNT_SERVICE='https://orbit-account-service.studypad.workers.dev';
// Full-screen login for a PC that is only a client: signs in at the account
// service, then shows the linked PC's projects as the Home orbit. Opening a
// project shows that PC's terminals in a separate window (no local bridge).
function showLoginScreen(){
  if($('#client-login'))return;
  const shell=el('section','client-login');shell.id='client-login';
  const header=el('header','client-login-header'),brand=el('div','home-brand'),mark=document.createElement('img');
  mark.className='orbit-wordmark-mark';mark.src='./orbit.svg';mark.alt='';brand.append(mark,el('span','','orbit'));
  const controls=el('div','window-controls home-window-controls');
  for(const [name,label,method] of [['minimize','최소화','windowMinimize'],['maximize','최대화 또는 복원','windowMaximize'],['close','닫기','windowClose']]){const b=el('button',name==='close'?'window-close':'',''); b.type='button';b.title=label;b.setAttribute('aria-label',label);b.append(icon(name));b.onclick=()=>notify(method);controls.append(b);}
  header.append(brand,controls);
  const form=el('form','client-login-form'),email=el('input','dialog-input'),password=el('input','dialog-input'),error=el('p','client-login-error'),hide=document.createElement('input'),hideLabel=el('label','client-login-hide');
  email.type='email';email.placeholder='이메일';email.autocomplete='username';email.value=state.settings.remoteClientEmail||'';
  password.type='password';password.placeholder='비밀번호';password.autocomplete='current-password';
  hide.type='checkbox';hideLabel.append(hide,document.createTextNode(' 시작할 때 이 화면을 표시하지 않기'));
  const login=el('button','primary','로그인'),skip=el('button','secondary','이 PC만 사용');login.type='submit';skip.type='button';
  error.setAttribute('role','alert');
  form.append(el('h1','','Orbit 로그인'),el('p','client-login-note','계정에 연결된 PC의 프로젝트를 이 PC에서 엽니다. 이 PC는 접속받는 PC로 등록되지 않고 Tailscale도 필요 없습니다.'),email,password,error,login,skip,hideLabel);
  let busy=false;
  form.onsubmit=async event=>{
    event.preventDefault();if(busy)return;busy=true;login.disabled=true;login.textContent='로그인 중…';error.textContent='';
    try{
      const result=await call('remoteClientLogin',{url:state.settings.remoteClientUrl||DEFAULT_ACCOUNT_SERVICE,email:email.value.trim(),password:password.value});
      state.settings.remoteClientEmail=email.value.trim();persist();
      password.value='';shell.remove();enterClientMode(result,email.value.trim());
    }catch(e){error.textContent=e.message||'로그인하지 못했습니다.';}
    finally{busy=false;login.disabled=false;login.textContent='로그인';}
  };
  skip.onclick=()=>{if(hide.checked){state.settings.startupLoginHidden=true;persist();}shell.remove();};
  shell.append(header,form);document.body.append(shell);email.value?password.focus():email.focus();
}
function enterClientMode(result,emailValue){
  const projects=(Array.isArray(result?.projects)?result.projects:[]).filter(item=>item&&typeof item.path==='string');
  state.client={email:emailValue,projects,names:Object.fromEntries(projects.map(item=>[item.path,typeof item.name==='string'&&item.name?item.name:basename(item.path)]))};
  document.body.classList.add('client-mode');showHome();
}
async function leaveClientMode(){
  await call('remoteClientLogout').catch(()=>{});
  state.client=null;document.body.classList.remove('client-mode');renderHomeOrbit();showLoginScreen();
}
async function refreshClientProjects(){
  try{enterClientMode(await call('remoteClientProjects'),state.client?.email);}
  catch(e){toast(e.message,true);}
}
const openProject = path => state.client ? call('remoteClientOpen',{project:path}) : setFolder(path);
async function remoteDevices(){
  let status=await call('remoteStatus'),tunnel=await call('remoteTunnelStatus').catch(()=>({})),tailscale=await call('remoteTailscaleStatus').catch(()=>({})),account=await call('accountStatus').catch(()=>({})),connecting=false,closed=false,connectionError=tailscale.error||tunnel.error||'',accountBusy=false,accountError=account.error||'';
  let render=()=>{};const merge=result=>{if(result&&result.status)status=result.status;if(result&&result.tunnel)tunnel=result.tunnel;if(result&&result.tailscale)tailscale=result.tailscale;};
  const unsub=on('remoteStatus',data=>{if(!closed){status=data.status;render();}});
  const tunnelUnsub=on('remoteTunnel',data=>{if(closed)return;tunnel=data.tunnel||{url:data.url,error:data.error,starting:false};connecting=!!tunnel.starting;if(data.error)connectionError=data.error;render();});
  const tailscaleUnsub=on('remoteTailscale',data=>{if(closed)return;tailscale=data.tailscale||{};connecting=!!tailscale.starting;if(tailscale.error)connectionError=tailscale.error;render();});
  const accountUnsub=on('remoteAccount',data=>{if(closed)return;account=data.account||{};accountError=account.error||'';render();});
  try { await dialog('외부 연결',node=>{
    const intro=el('p','remote-intro'),summary=el('p','muted'),error=el('p','dialog-note'),setupNote=el('p','dialog-note'),setupActions=el('div','remote-actions remote-setup-actions'),accountSection=el('section','remote-account-section'),accountTitle=el('h3','','계정'),accountBox=el('div',''),advanced=document.createElement('details');advanced.className='remote-advanced';
    const revisitSection=el('section','remote-revisit-section'),revisitTitle=el('h3','','다시 접속하기'),revisitUrl=el('input','dialog-input'),revisitRow=el('div','remote-actions'),revisitCopy=button('주소 복사','secondary',guard(async()=>{if(revisitUrl.value){await call('clipboard',{text:revisitUrl.value});toast('접속 주소를 복사했습니다.');}})),revisitNote=el('p','dialog-note','이 PC의 브라우저에서 이 주소를 북마크해 두면 바로 다시 열 수 있습니다.');
    revisitUrl.readOnly=true;revisitUrl.placeholder='주소를 준비하는 중입니다.';revisitRow.append(revisitUrl,revisitCopy);revisitSection.append(revisitTitle,revisitRow,revisitNote);
    // Account creation now turns the tunnel on by itself (see ensurePublicOrigin
    // below), so this manual button is just a fallback for reconnecting or
    // troubleshooting without touching the account -- it lives in 고급, not
    // as a separate primary step.
    const start=button('외부 주소 준비','secondary',guard(async()=>{if(connecting)return;connectionError='';connecting=true;render();try{merge(await call('remoteConnectStart',{port:49821}));state.settings.remoteAutoConnect=true;persist();}catch(e){connectionError=e.message;throw e;}finally{connecting=!!(tunnel.starting||tailscale.starting);if(!closed)render();}})),stop=button('외부 접속 끄기','secondary',guard(async()=>{connectionError='';state.settings.remoteAutoConnect=false;await persist();await call('remoteStop',{});status=await call('remoteStatus');tunnel=await call('remoteTunnelStatus').catch(()=>({}));tailscale=await call('remoteTailscaleStatus').catch(()=>({}));render();}));
    start.id='remote-connect-start';
    const email=el('input','dialog-input'),password=el('input','dialog-input');email.type='email';email.placeholder='이메일';email.autocomplete='username';password.type='password';password.placeholder='비밀번호 (8자 이상)';password.autocomplete='current-password';
    const accountErrorNote=el('p','dialog-note');
    const ensurePublicOrigin=async()=>{
      if((status.url||'').indexOf('https:')===0)return true;
      if(connecting)return false;
      connectionError='';connecting=true;render();
      try{merge(await call('remoteConnectStart',{port:49821}));state.settings.remoteAutoConnect=true;persist();}
      catch(e){connectionError=e.message;}
      finally{connecting=!!(tunnel.starting||tailscale.starting);if(!closed)render();}
      return (status.url||'').indexOf('https:')===0;
    };
    // Account creation implies wanting remote access, so it also turns on the
    // tunnel (and its auto-reconnect-on-launch) instead of requiring a separate
    // "외부 주소 준비" click first -- otherwise this would register a useless
    // loopback address with the account service.
    const runAccount=method=>guard(async()=>{
      if(accountBusy)return;
      accountBusy=true;accountError='';render();
      try{
        if(!await ensurePublicOrigin()){accountError='외부 주소를 아직 준비하지 못했습니다. 상태: '+(connectionError||'준비 중입니다. 잠시 후 다시 시도하세요.');return;}
        const r=await call(method,{email:email.value,password:password.value});
        if(!r.ok)accountError=r.error||'요청이 거부되었습니다.';else password.value='';
      }finally{accountBusy=false;account=await call('accountStatus').catch(()=>account);render();}
    });
    const signUp=button('계정 만들기','secondary',runAccount('accountSignUp')),logIn=button('이 계정에 연결','primary',runAccount('accountLink'));
    const unlink=button('연결 해제','secondary',guard(async()=>{accountBusy=true;render();try{await call('accountUnlink',{});}finally{accountBusy=false;account=await call('accountStatus').catch(()=>account);render();}}));
    const serviceUrl=el('input','dialog-input');serviceUrl.placeholder='계정 서비스 주소 (예: https://orbit-account.example.workers.dev)';serviceUrl.value=account.serviceUrl||'';
    serviceUrl.onchange=guard(async()=>{await call('accountServiceUrl',{url:serviceUrl.value.trim()});account=await call('accountStatus').catch(()=>account);render();});
    const advancedSummary=el('summary','', '고급');const publicUrl=el('input','dialog-input');publicUrl.value=state.settings.remoteOrigin||'';publicUrl.placeholder='고정 공개 HTTPS 주소 (선택)';publicUrl.onchange=guard(async()=>{connectionError='';state.settings.remoteOrigin=publicUrl.value.trim();await call('settings',state.settings);await call('remotePublicOrigin',{url:state.settings.remoteOrigin});status=await call('remoteStatus');render();});
    const fallback=button('임시 Cloudflare 연결 사용','secondary',guard(async()=>{if(connecting)return;connectionError='';connecting=true;render();try{merge(await call('remoteTunnelStart',{port:49821}));}finally{connecting=!!(tunnel.starting||tailscale.starting);render();}}));
    const diagnosis=button('연결 상태 확인','secondary',guard(async()=>{const result=await call('remoteDiagnostic');connectionError=result.checkedUrl?(result.reachable?'공개 모바일 주소를 확인했습니다.':('공개 모바일 주소에 연결하지 못했습니다. '+(result.error||''))):'현재 공개 HTTPS 주소가 없습니다.';render();}));
    const setup=button('Tailscale 로그인 계속','primary',guard(()=>call('remoteTailscaleSetup',{url:tailscale.setupUrl})));
    const install=button('Tailscale 설치 열기','primary',guard(()=>call('remoteTailscaleSetup',{url:'https://tailscale.com/download/windows'})));
    const advancedActions=el('div','remote-actions');advancedActions.append(start,fallback,diagnosis);
    const stopNote=el('p','dialog-note remote-stop-note','외부 접속을 끄면 현재 원격 연결은 종료됩니다. 계정 연결은 그대로 남습니다.');
    advanced.append(advancedSummary,el('p','dialog-note','계정 만들기·연결 버튼이 외부 주소를 알아서 켭니다. 아래는 수동 재연결, 계정 서비스 주소, 고정 공개 HTTPS 주소, 임시 Cloudflare 주소, 문제 해결용입니다.'),serviceUrl,publicUrl,advancedActions,revisitSection,stopNote,stop);
    setupActions.append(setup,install);
    render=()=>{
      const https=(status.url||'').indexOf('https:')===0;
      const waiting=connecting||tunnel.starting||tailscale.starting;
      const needsLogin=!https&&!!tailscale.setupUrl&&!waiting;
      const needsInstall=!https&&!tailscale.setupUrl&&/설치되어 있지 않습니다/.test(tailscale.error||'')&&!waiting;
      intro.textContent=https?'이 PC는 외부 접속을 받을 준비가 되었습니다.':'휴대폰이나 다른 기기에서 이 PC의 작업 공간을 열 수 있습니다.';
      summary.textContent=waiting?(tunnel.starting?'임시 Cloudflare 주소를 준비하고 있습니다.':'외부 주소를 준비하고 있습니다.'):https?(account.linked?'다른 기기에서 같은 계정으로 로그인하면 이 PC로 바로 연결됩니다.':'아래에서 계정을 연결하면 다른 기기에서 로그인해 바로 붙을 수 있습니다.'):'아래 계정에서 만들기·연결을 누르면 외부 주소까지 자동으로 준비됩니다. Tailscale 승인이 필요하면 브라우저에서 마친 뒤 Orbit으로 돌아오세요.';
      setupNote.hidden=!(needsLogin||needsInstall);
      setupNote.textContent=needsLogin?'Tailscale 로그인이 필요합니다. 브라우저에서 승인한 뒤 Orbit으로 돌아오세요.':needsInstall?'Tailscale 설치가 필요합니다. 설치한 뒤 다시 외부 주소를 준비하세요.':'';
      error.hidden=!connectionError;error.textContent=connectionError?'연결 상태: '+connectionError:'';
      setup.hidden=!needsLogin;install.hidden=!needsInstall;setupActions.hidden=!(needsLogin||needsInstall);
      start.disabled=waiting;start.textContent=waiting?'외부 주소 준비 중…':https?'외부 주소 새로고침':'외부 주소 준비';
      stop.disabled=!status.enabled;
      revisitSection.hidden=!https;revisitUrl.value=status.url||'';revisitCopy.disabled=!https;
      accountBox.replaceChildren();
      if(account.linked){
        const row=el('div','remote-account-row'),details=el('div','');
        details.append(el('strong','',account.email),el('small','','이 PC에 연결됨'));
        unlink.disabled=accountBusy;
        row.append(details,unlink);
        accountBox.append(row);
      } else {
        email.disabled=accountBusy;password.disabled=accountBusy;signUp.disabled=accountBusy;logIn.disabled=accountBusy;
        const row=el('div','remote-actions');row.append(signUp,logIn);
        accountBox.append(email,password,row);
      }
      accountErrorNote.hidden=!accountError;accountErrorNote.textContent=accountError;
      accountBox.append(accountErrorNote);
    };
    const clientSection=el('section','remote-account-section'),clientOpen=button('다른 PC에 접속','primary',()=>{$('#modal').close('client');showLoginScreen();});
    clientSection.append(el('h3','','다른 PC에 접속'),el('p','dialog-note','같은 계정을 연결해 둔 다른 PC를 이 앱에서 엽니다. 이 PC를 접속받는 PC로 등록하지 않으며 Tailscale도 필요 없습니다.'),clientOpen);
    stop.classList.add('remote-stop');accountSection.append(accountTitle,accountBox);node.append(intro,summary,clientSection,accountSection,error,setupNote,setupActions,advanced);render();
  });
  }finally{closed=true;unsub();tunnelUnsub();tailscaleUnsub();accountUnsub();}
}
async function preferences(){
  await dialog('나에게 맞는 작업 공간',node=>{
    const mode=el('label','setting-row');const check=el('input');check.type='checkbox';check.checked=state.settings.lowPower;mode.append(el('span','','절약 모드 · 터미널 기록 500줄'),check);check.onchange=()=>{state.settings.lowPower=check.checked;apply();};
    const font=el('label','setting-row');const size=el('select');for(const n of [11,12,13,14,15,16,18,20]){const o=el('option','',`${n}px`);o.value=n;o.selected=n===state.settings.fontSize;size.append(o);}font.append(el('span','','터미널 글자 크기'),size);size.onchange=()=>{state.settings.fontSize=+size.value;apply();};
    const shell = el('label', 'setting-row');
    const shellSelect = el('select');
    for (const [value, label] of [['powershell', 'PowerShell'], ['cmd', 'CMD']]) {
      const option = el('option', '', label);
      option.value = value;
      option.selected = value === state.settings.defaultShell;
      shellSelect.append(option);
    }
    shell.append(el('span', '', '기본 터미널'), shellSelect);
    shellSelect.id = 'default-shell';
    shellSelect.onchange = () => {
      state.settings.defaultShell = shellSelect.value;
      persist();
    };
    const theme = el('label', 'setting-row');
    const themeSelect = el('select');
    themeSelect.id = 'theme-mode';
    for (const [value, label] of [['dark', '다크 · Orbit'], ['light', '라이트 · Orbit']]) {
      const option = el('option', '', label);
      option.value = value;
      option.selected = value === state.settings.theme;
      themeSelect.append(option);
    }
    theme.append(el('span', '', '화면 테마'), themeSelect);
    themeSelect.onchange = () => { state.settings.theme = themeSelect.value; apply(); };
    const hidden=el('label','setting-row');const hiddenCheck=el('input');hiddenCheck.type='checkbox';hiddenCheck.checked=state.hidden;hidden.append(el('span','','숨김 폴더 · node_modules 표시'),hiddenCheck);hiddenCheck.onchange=guard(async()=>{state.hidden=hiddenCheck.checked;await refreshTree();});
    const usage=el('div','usage-result','필요할 때만 사용량을 측정합니다.');const measure=button('앱 호스트 사용량 측정','secondary',guard(async()=>{measure.disabled=true;try{const m=await call('metrics');usage.textContent=`메모리 ${m.hostMemoryMb} MB · CPU ${m.hostCpu}%\n${m.note}`;}finally{measure.disabled=false;}}));
    node.append(mode,font,shell,theme,hidden,el('h3','','리소스 사용'),el('p','muted','작업 폴더를 상시 감시하거나 파일 전체를 색인하지 않습니다. 실행 중인 에이전트의 CPU·메모리 사용량은 해당 CLI와 작업에 따라 달라집니다.'),measure,usage,el('p','dialog-note','파일 탭 최대 8개 · 파일당 4MB · 터미널 최대 8개\n터미널을 닫으면 연결된 하위 프로세스도 종료됩니다.'));
    function apply(){updatePower();applyTheme();persist();}
  });
}
function commands(){
  const actions=[['새 기본 터미널','Ctrl Shift T',()=>newTerminal()],['Codex 실행','',()=>newTerminal('codex')],['Claude 실행','',()=>newTerminal('claude')],['파일 열기','Ctrl O',openFilePicker],['작업 폴더 열기','',pickFolder],['파일 저장','Ctrl S',saveFile],['터미널 분할','Ctrl Shift D',toggleSplit],['프롬프트 보관함','',promptLibrary],['파일 새로고침','',refreshTree],['설정 및 사용량','',preferences]];
  dialog('명령 찾기',node=>{const input=el('input','dialog-input');input.placeholder='무엇을 할까요?';const list=el('div','command-list');const render=()=>{list.replaceChildren();for(const [label,key,action] of actions.filter(a=>a[0].toLowerCase().includes(input.value.toLowerCase()))){const b=button('','command-item',guard(async()=>{$('#modal').close();await action();}));b.append(el('span','',label),el('kbd','',key));list.append(b);}};input.oninput=render;input.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();list.querySelector('button')?.click();}};node.append(input,list);render();});
}
document.addEventListener('keydown',e=>{
  if(!e.ctrlKey || e.altKey)return;
  if(document.querySelector('dialog[open]'))return;
  const action=e.shiftKey?({KeyT:()=>newTerminal(),KeyD:toggleSplit,KeyW:()=>activeTerminal()&&closeTerminal(activeTerminal()),KeyF:terminalSearch})[e.code]:({KeyO:openFilePicker,KeyS:saveFile,KeyK:commands,KeyP:commands,KeyB:()=>app.classList.toggle('sidebar-hidden')})[e.code];
  if(action){e.preventDefault();guard(action)();}
});
const resizer=$('#editor-resizer');
resizer.addEventListener('pointerdown',event=>{resizer.setPointerCapture(event.pointerId);resizer.classList.add('dragging');});
resizer.addEventListener('pointermove',event=>{if(!resizer.hasPointerCapture(event.pointerId))return;const area=$('#work-area').getBoundingClientRect();const percent=Math.max(25,Math.min(70,(area.right-event.clientX)/area.width*100));$('#editor-section').style.width=`${percent}%`;});
resizer.addEventListener('pointerup',event=>{resizer.releasePointerCapture(event.pointerId);resizer.classList.remove('dragging');});
resizer.addEventListener('keydown',event=>{if(!['ArrowLeft','ArrowRight'].includes(event.key))return;event.preventDefault();const editor=$('#editor-section');const current=editor.clientWidth/$('#work-area').clientWidth*100;editor.style.width=`${Math.max(25,Math.min(70,current+(event.key==='ArrowLeft'?3:-3)))}%`;});
window.addEventListener('beforeunload',event=>{if(state.files.some(f=>f.dirty)){event.preventDefault();event.returnValue='';}});
// Integration-test observations are read-only, and the desktop keeps remote navigation disabled.
const layoutSnapshot=node=>node.type==='group'?{type:'group',id:node.id,tabs:[...node.tabs],active:node.active}:{type:'split',id:node.id,direction:node.direction,ratio:node.ratio,first:layoutSnapshot(node.first),second:layoutSnapshot(node.second)};
window.orbitDiagnostics=()=>({ready:!!state.folder,activeGroup:state.activeGroup,layout:layoutSnapshot(state.terminalRoot),sessions:state.terminals.map(t=>{
  const buffer=t.pane.term.buffer.active, first=Math.max(0,buffer.baseY+buffer.cursorY-80), lines=[];
  for(let y=first;y<buffer.length;y++) lines.push(buffer.getLine(y)?.translateToString(true) || '');
  return {id:t.id,profile:t.profile,resumeId:t.resumeId,pid:t.pane.pid,exited:t.pane.exited,cols:t.pane.term.cols,rows:t.pane.term.rows,outputCount:t.pane.outputCount,output:lines.join('\n')};
}),files:state.files.map(f=>({name:f.name,dirty:f.dirty})),editor:!!state.editor,windowControls:document.querySelectorAll('.window-controls button').length});
// Used only by the native --ui-test harness. It drives the same public UI state,
// bridge calls, CodeMirror view and xterm input used by a normal desktop session.
window.orbitUiTest={
  openFile: path => openFile(path),
  editAndSave: async text => {
    if(!state.editor || !state.file) throw new Error('editor did not open');
    state.editor.dispatch({changes:{from:0,to:state.editor.state.doc.length,insert:text}});
    if(!state.file.dirty) throw new Error('editor dirty state did not change');
    await Promise.all([saveFile(),saveFile(),saveFile()]);
    return {dirty:state.file.dirty, name:state.file.name};
  },
  editContent: text => {
    if(!state.editor || !state.file) throw new Error('editor did not open');
    state.editor.dispatch({changes:{from:0,to:state.editor.state.doc.length,insert:text}});
  },
  save: () => saveFile(),
  exerciseEditorConcurrency: async paths => {
    await Promise.all(paths.map(path=>openFile(path)));
    if(document.querySelectorAll('#editor-host .cm-editor').length!==1)throw new Error('duplicate editor views');
    if(state.file.path!==paths.at(-1))throw new Error('file operations completed out of order');
    const first=state.files[0],last=state.file;
    await Promise.all([activateFile(first),activateFile(last),activateFile(first),activateFile(last)]);
    if(state.file!==last || document.querySelectorAll('#editor-host .cm-editor').length!==1)throw new Error('rapid tab switch failed');
    await Promise.all(state.files.filter(file=>file!==last).map(file=>closeFile(file)));
    return true;
  },
  startCmd: () => newTerminal('cmd'),
  startPowerShell: () => newTerminal('powershell'),
  refreshSaved: () => refreshSavedSessions(),
  deleteSaved: item => deleteSavedSession(item),
  setFolder: path => setFolder(path),
  addHomeProject: path => addRecentProject(path),
  closeAll: async () => { for(const t of [...state.terminals]) { await call('closeTerminal',{session:t.id});removeTab(t.id);t.pane.dispose();state.terminals=state.terminals.filter(x=>x!==t); } renderTerminals(); },
  savedSessions: () => state.savedSessions.map(x=>({provider:x.provider,id:x.id,cwd:x.cwd,title:x.title})),
  split: () => toggleSplit(),
  send: async text => { const terminal=activeTerminal(); if(!terminal) throw new Error('terminal did not open'); await terminal.pane.paste(text); },
  diagnostics: () => ({...window.orbitDiagnostics(),file:state.file?{path:state.file.path,dirty:state.file.dirty,mode:state.file.markdownMode||'edit',content:state.editor?.state.sliceDoc()}:null})
};
guard(initialize)();
