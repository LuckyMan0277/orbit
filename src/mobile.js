import { Terminal } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';
import './mobile.css';
import { conversationDetails } from './remote-readable.js';
import { composePackets, composeStageKey, epochChanged, sendComposerPackets } from './remote-input.js';
import { remainingDeadlineMs } from './remote-connection.js';

const $ = selector => document.querySelector(selector);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const profiles = { codex:'Codex', claude:'Claude', powershell:'PowerShell', cmd:'명령 프롬프트' };
const specialKeys = { 'ctrl-c':'\u0003', enter:'\r', tab:'\t', escape:'\u001b', up:'\u001b[A', down:'\u001b[B', left:'\u001b[D', right:'\u001b[C' };
const maxInput = 8192;
let storageBlocked = false;
function stored(key) { try { return localStorage.getItem(key) || ''; } catch { storageBlocked = true; return ''; } }
function saveCredentials(nextToken) { try { localStorage.setItem('orbit.remote.token', nextToken); return true; } catch { storageBlocked = true; return false; } }
function forgetCredentials() { try { localStorage.removeItem('orbit.remote.token'); } catch { storageBlocked = true; } }
function savePreference(key, value) { try { localStorage.setItem(key, value); } catch { storageBlocked = true; } }
let token = stored('orbit.remote.token');
let serverEpoch = '', session = '', readySession = '', sessions = [], savedSessions = [], seq = 0;
let projects = [], project = stored('orbit.remote.project'), currentProject = '';
let run = 0, pollAbort = null, reconnecting = false, available = true, retryTimer = 0, connectionOpen = false;
let view = stored('orbit.remote.view') || 'readable';
let themeChoice = stored('orbit.remote.theme') || 'system';
let toastTimer = 0, inputTail = Promise.resolve(), composerBusy = false, renderTimer = 0, forceReadableFollow = false, composeGeneration = 0;
const drafts = new Map();
const uncertain = new Map();
const composeStages = new Map();
const term = new Terminal({ disableStdin:true, scrollback:1200, fontFamily:'Cascadia Mono, Consolas, monospace', fontSize:13 });

function apiError(message, status = 0) { const value = new Error(message); value.status = status; return value; }
function state(text, bad = false) { $('#state').textContent = text; $('#state').classList.toggle('bad', bad); }
function toast(text) { $('#toast').textContent = text; $('#toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('#toast').hidden = true; }, 3000); }
function currentTheme() { return themeChoice === 'system' ? (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark') : themeChoice; }
function applyTheme() {
  const light = currentTheme() === 'light';
  document.documentElement.dataset.theme = light ? 'light' : 'dark';
  $('#theme').value = themeChoice;
  term.options.theme = light ? { background:'#ffffff', foreground:'#302d38', cursor:'#7958ae', selectionBackground:'#dcd3ef' } : { background:'#111118', foreground:'#e5e2ef', cursor:'#cbb8ff', selectionBackground:'#57466f' };
}
function updateViewport() { document.documentElement.style.setProperty('--app-height', `${visualViewport?.height || innerHeight}px`); }
async function api(path, body = {}, auth = true, signal, accessToken = token, timeout = 12000) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  const timer = setTimeout(abort, timeout);
  signal?.addEventListener('abort', abort, { once:true });
  try {
    const response = await fetch(`/api/v1/${path}`, { method:'POST', cache:'no-store', signal:controller.signal, headers:{ 'content-type':'application/json', ...(auth && accessToken ? { authorization:`Bearer ${accessToken}` } : {}) }, body:JSON.stringify(body) });
    let value;
    try { value = await response.json(); } catch { throw apiError(`서버 응답을 읽을 수 없습니다. (HTTP ${response.status || '없음'}, /api/v1/${path})`, response.status); }
    if (!response.ok) {
      if (auth && response.status === 401 && accessToken === token) forget('PC에서 이 브라우저 등록을 해제했거나 권한이 만료되었습니다.');
      const failure = apiError(value.error || '요청을 완료할 수 없습니다.', response.status); failure.serverEpoch = value.serverEpoch; throw failure;
    }
    return value;
  } catch (failure) {
    if (failure.name === 'AbortError' && signal?.aborted) throw failure;
    if (failure.name === 'AbortError') throw apiError('응답 시간이 초과되었습니다.');
    throw failure;
  } finally { clearTimeout(timer); signal?.removeEventListener('abort', abort); }
}
function invalidateComposeState() { composeGeneration++; composeStages.clear(); uncertain.clear(); }
function checkEpoch(value) {
  if (!value?.serverEpoch) return true;
  const changed = epochChanged(serverEpoch, value.serverEpoch);
  serverEpoch = value.serverEpoch;
  if (changed) invalidateComposeState();
  return !changed;
}
function saveDraft() { if (session) drafts.set(session, $('#input').value); }
function restoreDraft() { $('#input').value = drafts.get(session) || ''; }
function controls() {
  const okay = available && !!session && readySession === session && !composerBusy;
  $('#send').disabled = !okay; $('#input-only').disabled = !okay; $('#stop').disabled = !okay;
  document.querySelectorAll('[data-key]').forEach(button => { button.disabled = !okay; });
  document.querySelectorAll('[data-create]').forEach(button => { button.disabled = !available; });
  const status = $('#conversation-status'), composer = $('#composer-status');
  const busy = !!session && readySession !== session;
  status.dataset.state = available ? (busy ? 'busy' : 'ready') : 'offline';
  status.textContent = available ? (busy ? '세션 불러오는 중' : '연결됨') : '연결 끊김';
  composer.textContent = !session ? '세션을 선택하세요' : !available ? '연결을 복구하는 중' : busy ? '세션 준비 중' : composerBusy ? '전송 확인 중' : '입력 준비됨';
}
function sessionName(item) { return item.name || item.title || profiles[item.profile] || '터미널'; }
function truncateName(text, max = 22) { const value = String(text || ''); return value.length > max ? `${value.slice(0, max - 3).trimEnd()}...` : value; }
function renderTabs() {
  $('#session-tabs').replaceChildren(...sessions.map(item => { const button = document.createElement('button'); button.className = `session-tab${item.id === session ? ' active' : ''}`; const full = sessionName(item); button.textContent = truncateName(full); button.title = full; button.onclick = () => { selectSession(item.id); closeMenu(); }; return button; }));
  $('#session-select').replaceChildren(...sessions.map(item => { const full = sessionName(item); const option = new Option(truncateName(full), item.id, false, item.id === session); option.title = full; return option; }));
  $('#empty').hidden = !!session; $('#workspace').classList.toggle('is-empty', !session); controls();
  $('#output-title').textContent = session ? truncateName(sessionName(sessions.find(item => item.id === session) || {})) : '현재 터미널';
}
function renderSaved() { const box=$('#saved-list'),query=($('#saved-filter')?.value||'').toLowerCase(),rows=savedSessions.filter(item=>(!project||(item.cwd||'').toLowerCase()===project.toLowerCase())&&`${item.title} ${item.cwd} ${item.provider}`.toLowerCase().includes(query)); if(!rows.length){box.replaceChildren(Object.assign(document.createElement('p'),{textContent:query?'일치하는 이전 대화가 없습니다.':'저장된 이전 대화가 없습니다.'}));return;} box.replaceChildren(...rows.map(item=>{const row=document.createElement('div'),button=document.createElement('button'),remove=document.createElement('button'),date=item.updated?new Date(item.updated).toLocaleDateString('ko-KR'):'';row.className='saved-row';button.className='saved-open';button.title=item.cwd||'';button.append(Object.assign(document.createElement('strong'),{textContent:item.title}),Object.assign(document.createElement('small'),{textContent:`${profiles[item.provider]||item.provider} · ${item.cwd}${date?` · ${date}`:''}`}));button.onclick=()=>createSession(item.provider,item);remove.className='saved-delete';remove.textContent='×';remove.title=`${item.title} 영구 삭제`;remove.setAttribute('aria-label',`${item.title} 영구 삭제`);remove.onclick=async()=>{if(remove.disabled)return;remove.disabled=true;try{await deleteSaved(item);}catch(failure){state(failure.message||'대화를 삭제하지 못했습니다.',true);toast(failure.message||'대화를 삭제하지 못했습니다.');}finally{remove.disabled=false;}};row.append(button,remove);return row;})); }
function renderProjects() { const row=$('#project-row'),select=$('#project-select'); if(!row||!select)return; if(!projects.length){row.hidden=true;return;} row.hidden=false; select.replaceChildren(...projects.map(item=>new Option(item.name+(item.path===currentProject?' (현재)':''),item.path,false,item.path===project))); }
async function loadProjects(access=token) { try { const data=await api('projects',{},true,undefined,access); if(access!==token)return; projects=Array.isArray(data.projects)?data.projects:[]; currentProject=data.current||''; if(!project||!projects.some(item=>item.path===project))project=currentProject||projects[0]?.path||''; savePreference('orbit.remote.project',project); renderProjects(); renderSaved(); } catch { /* keep the last known project list on failure */ } }
async function deleteSaved(item) { if(!window.confirm(`“${item.title}” 대화를 영구 삭제합니다. 이 작업은 되돌릴 수 없습니다.`)) return false; const data=await api('saved-sessions/delete',{provider:item.provider,id:item.id});savedSessions=Array.isArray(data.sessions)?data.sessions:[];renderSaved();toast('대화를 영구 삭제했습니다.');return true; }
async function loadSavedSessions(access=token) { const data=await api('saved-sessions',{},true,undefined,access);if(access!==token)return;savedSessions=Array.isArray(data.sessions)?data.sessions:[];renderSaved(); }
function readableConversation() { return conversationDetails(term.buffer.active, sessions.find(item => item.id === session)?.profile || '', 700); }
function atOutputEnd() { const box = $('#readable-output'); return box.scrollTop + box.clientHeight >= box.scrollHeight - 24; }
function renderReadable() {
  const details = readableConversation(), active = sessions.find(item => item.id === session);
  if (active?.profile) $('#session-profile').textContent = active.profile === 'codex' || active.profile === 'claude' ? `${profiles[active.profile] || active.profile} · ${details.model || '모델 알 수 없음'}` : (profiles[active.profile] || active.profile);
  if (view !== 'readable') return;
  const box = $('#readable-output'), follow = forceReadableFollow || atOutputEnd();
  forceReadableFollow = false;
  const text = details.text, message = document.createElement('article'), content = document.createElement('pre');
  message.className = `output-message${text ? '' : ' empty'}`;
  content.textContent = text || '아직 표시할 출력이 없습니다.';
  message.append(content); box.replaceChildren(message);
  if (follow) box.scrollTop = box.scrollHeight; $('#latest').hidden = follow;
}
function queueReadableRender() { if (renderTimer) return; renderTimer = setTimeout(() => { renderTimer = 0; renderReadable(); }, 80); }
function setView(next) { view = next; savePreference('orbit.remote.view', view); $('#readable-output').hidden = view !== 'readable'; $('#raw-output').hidden = view !== 'raw'; $('#view-toggle').textContent = view === 'readable' ? '원본 터미널' : '읽기 보기'; queueReadableRender(); }
function writeTerminal(data) { return new Promise(resolve => term.write(data || '', resolve)); }
function valid(expected, id, access) { return expected === run && id === session && token === access; }
async function snapshot(expected, id, access = token, deadline = 0) {
  const timeout = deadline ? remainingDeadlineMs(deadline) : 12000;
  if (!timeout) throw apiError('응답 시간이 초과되었습니다.');
  const data = await api('snapshot', { session:id }, true, undefined, access, timeout);
  if (!valid(expected, id, access)) return false;
  checkEpoch(data); term.reset(); forceReadableFollow = true;
  // It sizes the local renderer only; no resize is ever sent to the PC terminal.
  if (data.cols && data.rows) term.resize(data.cols, data.rows);
  await writeTerminal(data.data || ''); if (!valid(expected, id, access)) return false;
  for (const item of data.items || []) { await writeTerminal(item.data || ''); if (!valid(expected, id, access)) return false; }
  seq = data.seq || 0; readySession = id; available = true;
  const active = sessions.find(item => item.id === id);
  $('#session-profile').textContent = active?.profile ? `${profiles[active.profile] || active.profile} · 모델 확인 중` : '세션 정보 없음';
  controls(); queueReadableRender(); return true;
}
async function loadSessions(expected = run, preferred = '', access = token, deadline = 0) {
  const timeout = deadline ? remainingDeadlineMs(deadline) : 12000;
  if (!timeout) throw apiError('응답 시간이 초과되었습니다.');
  const data = await api('sessions', {}, true, undefined, access, timeout);
  if (expected !== run || access !== token) return false;
  const changed = !checkEpoch(data); available = true; sessions = Array.isArray(data.sessions) ? data.sessions : [];
  const keep = preferred || session; session = sessions.some(item => item.id === keep) ? keep : (sessions[0]?.id || ''); readySession = ''; seq = 0;
  renderTabs(); restoreDraft();
  if (!session) { $('#reconnect').hidden = true; controls(); state('실행 중인 터미널이 없습니다. 아래에서 새로 시작하세요.'); return false; }
  const loaded = await snapshot(expected, session, access, deadline); if (loaded) state(changed ? 'PC가 다시 시작되어 연결을 복원했습니다.' : '연결됨'); return loaded;
}
async function poll(expected = run, access = token) {
  const controller = pollAbort = new AbortController();
  while (!document.hidden && token === access && session && expected === run) {
    const id = session;
    try {
      const data = await api('output', { session:id, seq }, true, controller.signal, access, 30000);
      if (!valid(expected, id, access)) break;
      checkEpoch(data);
      if (data.reset) { if (!await snapshot(expected, id, access)) break; continue; }
      const follow = atOutputEnd();
      for (const item of data.items || []) { await writeTerminal(item.data || ''); if (!valid(expected, id, access)) return; }
      seq = data.seq || seq; available = true; controls(); queueReadableRender();
      if (follow) requestAnimationFrame(() => { if (valid(expected, id, access)) $('#readable-output').scrollTop = $('#readable-output').scrollHeight; });
      state('연결됨');
    } catch (failure) {
      if (failure.name === 'AbortError' || expected !== run || access !== token) break;
      available = false; controls(); state(failure.message || '연결이 끊겼습니다.', true); scheduleReconnect(expected, access, failure); break;
    }
  }
}
function reconnectFailure(expected, access, failure) {
  if (expected !== run || access !== token) return;
  saveDraft(); connectionOpen = false; available = false; composerBusy = false;
  clearTimeout(retryTimer); pollAbort?.abort();
  const detail = failure?.message || 'PC와의 연결이 끊겼습니다.';
  const message = `${detail} PC Orbit과 네트워크를 확인한 뒤 다시 연결하세요.`;
  $('#workspace').hidden = false; $('#reconnect').hidden = false; $('#reconnect p').textContent = message;
  state(message, true); controls();
}
function scheduleReconnect(expected = run, access = token, failure) {
  clearTimeout(retryTimer);
  reconnectFailure(expected, access, failure || apiError('연결을 복구하지 못했습니다.'));
}
async function refresh(startup = false) {
  if (reconnecting || document.hidden || !token || !connectionOpen) return;
  reconnecting = true; saveDraft(); run++; pollAbort?.abort(); const expected = run, access = token;
  if (startup) state('PC 연결을 확인하는 중입니다.');
  const deadline = startup ? Date.now() + 5000 : 0;
  try { if (access === token && await loadSessions(expected, session, access, deadline)) poll(expected, access); }
  catch (failure) { reconnectFailure(expected, access, failure); }
  finally { if (expected === run) reconnecting = false; }
}
async function selectSession(id) {
  if (!id || id === session) return;
  saveDraft(); run++; pollAbort?.abort(); const expected = run, access = token; session = id; readySession = ''; renderTabs(); restoreDraft();
  try { if (await snapshot(expected, id, access)) poll(expected, access); }
  catch (failure) { if (expected === run && access === token) scheduleReconnect(expected, access, failure); }
}
async function createSession(profile, saved = null) {
  if (!available || !token) return;
  saveDraft(); const expected = ++run, access = token; pollAbort?.abort(); available = false; controls();
  try {
    if (!saved) await loadProjects(access);
    if (expected !== run || access !== token) return;
    const created = await api('terminals/create', { profile, ...(saved ? {resumeId:saved.id} : (project ? {project} : {})) }, true, undefined, access); if (expected !== run || access !== token) return; if (await loadSessions(expected, created?.session || '', access) && session) poll(expected, access);
  }
  catch (failure) { if (expected === run && access === token) { available = true; controls(); state(failure.message, true); toast(failure.message || '세션을 만들지 못했습니다.'); } }
}
async function confirmInput(record, access) { try { return (await api('input/status', { session:record.session, data:record.data, requestId:record.id, serverEpoch:record.epoch }, true, undefined, access, 5000)).status === 'confirmed'; } catch { return false; } }
function queueInput(work) {
  const next = inputTail.then(work, work); inputTail = next.then(() => {}, () => {}); return next;
}
function sendInputNow(id, data) {
  if (!available || !token || !id || data.length > maxInput) return Promise.resolve(false);
  const key = `${id}\n${data}`, access = token, expected = run;
  const record = uncertain.get(key) || { session:id, data, id:`${Date.now()}-${Math.random().toString(36).slice(2, 10)}`, epoch:serverEpoch, uncertain:false };
  const work = async () => {
    if (!connectionOpen || expected !== run || access !== token) return false;
    if (record.uncertain) { if (await confirmInput(record, access)) { uncertain.delete(key); return true; } state('입력 처리 여부를 확인 중입니다. 같은 명령을 다시 보내지 않았습니다.', true); return false; }
    if (!record.epoch || record.epoch !== serverEpoch) { state('PC가 다시 시작되어 이전 입력을 보내지 않았습니다.', true); return false; }
    try { const result = await api('input', { session:id, data, requestId:record.id, serverEpoch:record.epoch }, true, undefined, access); return checkEpoch(result); }
    catch (failure) {
      if (!connectionOpen || access !== token) { record.uncertain = true; uncertain.set(key, record); return false; }
      const knownRefusal = failure.status === 401 || failure.status === 429 || (failure.status === 409 && /server epoch changed|request receipt expired|request id payload mismatch/i.test(failure.message));
      if (knownRefusal) { uncertain.delete(key); state(failure.message, true); return false; }
      record.uncertain = true; uncertain.set(key, record);
      if (await confirmInput(record, access)) { uncertain.delete(key); return true; }
      state('입력 처리 여부를 확인 중입니다. 같은 명령을 다시 보내지 않았습니다.', true); return false;
    }
  };
  return work();
}
function sendInput(id, data) {
  return queueInput(() => sendInputNow(id, data));
}
async function compose(submit) {
  if (composerBusy || !session || readySession !== session) return;
  const capturedSession = session, capturedText = $('#input').value;
  if (!capturedText) return;
  const packets = composePackets(capturedText, !!term.modes?.bracketedPasteMode, submit);
  if (packets.some(data => data.length > maxInput)) { toast(`입력은 ${maxInput}자까지 보낼 수 있습니다.`); return; }
  const stageKey = composeStageKey(composeGeneration, capturedSession, capturedText, submit);
  if (!composeStages.has(stageKey) && composeStages.size >= 12) { toast('보류 중인 전송이 많습니다. 전송 확인 후 다시 시도하세요.'); return; }
  const stage = composeStages.get(stageKey) || { bodySent:false };
  composeStages.set(stageKey, stage);
  composerBusy = true; controls();
  try {
    // Do not submit if the body receipt is indeterminate. A retry reuses this
    // stage and only verifies/sends the pending input before it reaches Enter.
    // Queue the pair as one unit so terminal special keys cannot interleave.
    if (await queueInput(() => sendComposerPackets(stage, packets, submit, data => sendInputNow(capturedSession, data)))) {
      composeStages.delete(stageKey);
      if (drafts.get(capturedSession) === capturedText) drafts.delete(capturedSession);
      if (session === capturedSession && $('#input').value === capturedText) $('#input').value = '';
    }
  }
  finally { composerBusy = false; controls(); saveDraft(); }
}
function parseLoginToken(value) {
  // The QR carries the token as ?login= (in-app browsers and some scanners drop or encode a #fragment); links from the
  // account login page still use #login=.
  try { const url = new URL(value); value = url.search + url.hash; } catch { /* fragment or invalid URL */ }
  const match = String(value || '').match(/(?:^|[?#&])login=([^&\s]+)/);
  if (!match) return null;
  try { return decodeURIComponent(match[1]) || null; } catch { return null; }
}
function showWorkspace() { if (!token) { connectionOpen = false; $('#boot').hidden = true; $('#reconnect').hidden = true; $('#workspace').hidden = true; $('#login').hidden = false; state(storageBlocked ? '브라우저 저장소가 차단되어 로그인 상태를 유지할 수 없습니다.' : '로그인이 필요합니다.'); return; } clearTimeout(retryTimer); if (reconnecting) { run++; pollAbort?.abort(); reconnecting = false; } connectionOpen = true; $('#boot').hidden = true; $('#login').hidden = true; $('#reconnect').hidden = true; $('#workspace').hidden = false; refresh(true); loadProjects().catch(() => {}); }
function disconnect(message = '연결을 일시 중지했습니다.') {
  connectionOpen = false; run++; pollAbort?.abort(); reconnecting = false; clearTimeout(retryTimer); clearTimeout(renderTimer); renderTimer = 0; session = ''; readySession = ''; sessions = []; savedSessions=[]; seq = 0; available = false; composerBusy = false;
  $('#boot').hidden = true; $('#login').hidden = true; $('#workspace').hidden = true; $('#reconnect').hidden = false; $('#reconnect p').textContent = message; state(message, true); controls();
}
function forget(message = 'PC에서 이 기기의 로그인을 해제했습니다.') {
  disconnect(message); invalidateComposeState(); serverEpoch = ''; token = ''; forgetCredentials(); $('#reconnect').hidden = true; $('#login').hidden = false; state(message, true);
}

term.open($('#raw-output'));
// Keep the composer to its primary send/stop actions. Less frequent terminal
// controls remain available in the collapsed options disclosure on every size.
const advancedKeybar = document.querySelector('.keybar'), advancedKeys = advancedKeybar?.querySelector('div'), enterRow = document.querySelector('.enter-row'), enterButton = document.querySelector('[data-key=enter]');
if (advancedKeybar && advancedKeys) {
  advancedKeybar.style.display = 'block'; advancedKeybar.querySelector('summary').textContent = '입력 옵션';
  // Do not nest the Enter control in a flex item: that collapses its hit area
  // beside Ctrl C on a keyboard-height viewport.
  advancedKeys.prepend($('#input-only'), enterButton); enterRow?.remove();
  advancedKeys.querySelectorAll('button').forEach(button => { button.style.flex = '0 0 auto'; button.style.minHeight = '40px'; });
}
applyTheme(); setView(view); updateViewport();
$('#send').onclick = () => compose(true); $('#input-only').onclick = () => compose(false); $('#stop').onclick = () => sendInput(session, specialKeys['ctrl-c']); $('#view-toggle').onclick = () => setView(view === 'readable' ? 'raw' : 'readable');
$('#latest').onclick = () => { $('#readable-output').scrollTop = $('#readable-output').scrollHeight; $('#latest').hidden = true; }; $('#session-select').onchange = event => { selectSession(event.target.value); closeMenu(); }; $('#refresh').onclick = showWorkspace; $('#resume').onclick = showWorkspace; $('#logout').onclick = () => disconnect(); $('#input').oninput = saveDraft;
$('#input').onkeydown = event => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && !event.isComposing) { event.preventDefault(); compose(true); } };
document.querySelectorAll('[data-key]').forEach(button => { button.onclick = () => sendInput(session, specialKeys[button.dataset.key]); }); document.querySelectorAll('[data-create]').forEach(button => { button.onclick = () => { $('#new-session').open = false; createSession(button.dataset.create); closeMenu(); }; });
$('#saved-sessions').ontoggle = () => { if($('#saved-sessions').open && token) loadSavedSessions().catch(failure=>state(failure.message,true)); };
$('#saved-filter').oninput = renderSaved;
$('#project-select').onchange = event => { project = event.target.value; savePreference('orbit.remote.project', project); renderSaved(); };
function closeMenu() { $('#menu-panel').hidden = true; }
$('#menu-toggle').onclick = () => { $('#menu-panel').hidden = !$('#menu-panel').hidden; };
$('#theme').onchange = event => { themeChoice = event.target.value; savePreference('orbit.remote.theme', themeChoice); applyTheme(); }; $('#readable-output').onscroll = () => { $('#latest').hidden = atOutputEnd(); };
visualViewport?.addEventListener('resize', updateViewport); window.addEventListener('resize', updateViewport); matchMedia('(prefers-color-scheme: light)').addEventListener('change', () => { if (themeChoice === 'system') applyTheme(); });
const hashToken = parseLoginToken(location.href);
// The desktop app's client mode opens a specific project of this PC via #project=.
const hashProject = (String(location.hash).match(/(?:^|[#&])project=([^&]+)/) || [])[1];
if (hashProject) { try { project = decodeURIComponent(hashProject); savePreference('orbit.remote.project', project); } catch { /* ignore a malformed value */ } }
if (hashToken) { if (token !== hashToken) { invalidateComposeState(); serverEpoch = ''; } token = hashToken; if (!saveCredentials(token)) { token = ''; storageBlocked = true; } history.replaceState(null, '', location.pathname); }
document.addEventListener('visibilitychange', () => { if (document.hidden) { pollAbort?.abort(); clearTimeout(retryTimer); } else refresh(); }); window.addEventListener('online', refresh); window.addEventListener('offline', () => { available = false; controls(); state('오프라인', true); });
if (token) { showWorkspace(); } else { $('#boot').hidden = true; $('#login').hidden = false; state(storageBlocked ? '브라우저 저장소가 차단되어 로그인 상태를 유지할 수 없습니다.' : '로그인이 필요합니다.'); } if ('serviceWorker' in navigator) navigator.serviceWorker.register('/mobile-sw.js').catch(() => {});

function initInstallBanner() {
  const banner = $('#install-banner'), text = $('#install-text'), action = $('#install-action'), dismiss = $('#install-dismiss');
  if (!banner || stored('orbit.remote.installDismissed')) return;
  const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  if (standalone) return;
  const isIOS = /iP(hone|ad|od)/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const hide = () => { banner.hidden = true; savePreference('orbit.remote.installDismissed', '1'); };
  dismiss.onclick = hide;
  let deferred = null;
  window.addEventListener('beforeinstallprompt', event => { event.preventDefault(); deferred = event; text.textContent = '홈 화면에 추가하면 아이콘으로 바로 열 수 있어요.'; action.hidden = false; banner.hidden = false; });
  action.onclick = async () => { if (!deferred) return; action.hidden = true; deferred.prompt(); deferred = null; banner.hidden = true; };
  if (isIOS) { text.textContent = '공유 버튼 → "홈 화면에 추가"를 누르면 아이콘으로 바로 열 수 있어요.'; banner.hidden = false; }
}
initInstallBanner();
