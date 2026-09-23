const host = window.chrome?.webview;
const waiting = new Map();
const listeners = new Map();
let nextId = 1;
export const isDesktop = !!host;
host?.addEventListener('message', ({ data }) => {
  if (data.id) {
    const entry = waiting.get(data.id);
    if (!entry) return;
    waiting.delete(data.id);
    data.error ? entry.reject(new Error(data.error)) : entry.resolve(data.result);
  } else for (const fn of listeners.get(data.type) || []) fn(data);
});
export function on(type, handler) {
  if (!listeners.has(type)) listeners.set(type, new Set());
  listeners.get(type).add(handler);
  return () => listeners.get(type).delete(handler);
}
export function notify(method, args = {}) { host?.postMessage({ id: 0, method, args }); }
export async function call(method, args = {}) {
  if (!host) return preview(method, args);
  return new Promise((resolve, reject) => {
    const id = nextId++;
    waiting.set(id, { resolve, reject });
    host.postMessage({ id, method, args });
  });
}
// Files dropped from Windows Explorer: WebView2 hands their real paths to the host alongside the message.
export const canSendFiles = typeof host?.postMessageWithAdditionalObjects === 'function';
export function callWithFiles(method, args, files) {
  if (!canSendFiles) return Promise.reject(new Error('이 환경에서는 탐색기에서 파일을 끌어올 수 없습니다.'));
  return new Promise((resolve, reject) => {
    const id = nextId++;
    waiting.set(id, { resolve, reject });
    host.postMessageWithAdditionalObjects({ id, method, args }, files);
  });
}
const previewDocs = {
  'README.md': '# Orbit\n\n에이전트 작업을 위한 가벼운 데스크톱 워크스페이스.\n\n## 시작하기\n\n1. 작업 폴더를 선택하세요.\n2. Codex, Claude 또는 터미널을 여세요.\n3. 파일 링크를 클릭해 바로 편집하세요.\n\n> 브라우저에서는 UI 미리보기만 사용할 수 있습니다.\n',
  'AGENTS.md': '# 프로젝트 안내\n\n- 변경 전 관련 파일을 읽으세요.\n- 작은 단위로 수정하고 테스트하세요.\n- 작업 결과를 한국어로 요약하세요.\n',
  'package.json': '{\n  "name": "orbit-workspace",\n  "private": true\n}\n'
};
const previewFolders = ['C:\\Workspace\\my-project', 'C:\\Workspace\\my-project\\docs', 'C:\\Workspace\\my-project\\src'];
const previewSettingsKey = 'orbit-preview-settings';
let previewSettings = null;
try { previewSettings = JSON.parse(localStorage.getItem(previewSettingsKey) || 'null'); } catch { /* preview can start without storage */ }
async function preview(method, args) {
  switch (method) {
    case 'init': return { folder: 'C:\\Workspace\\my-project', version: '0.1.0', settings: previewSettings };
    case 'list': return { entries: Object.keys(previewDocs).map(name => ({ name, path: `C:\\Workspace\\my-project\\${name}`, directory: false })), truncated: false };
    case 'pickerPlaces': return { home: 'C:\\Users\\Orbit', desktop: 'C:\\Users\\Orbit\\Desktop', documents: 'C:\\Users\\Orbit\\Documents', downloads: 'C:\\Users\\Orbit\\Downloads', drives: [{ name: 'C:', path: 'C:\\' }] };
    case 'browse': {
      const path = args.path || 'C:\\Workspace\\my-project';
      if (path.includes('missing')) throw new Error('폴더를 찾을 수 없습니다.');
      const folders = previewFolders.filter(folder => folder.startsWith(path + '\\')).filter(folder => !folder.slice(path.length + 1).includes('\\')).map(folder => ({ name: folder.split('\\').at(-1), path: folder, directory: true }));
      const files = args.directoriesOnly ? [] : Object.keys(previewDocs).map(name => ({ name, path: `${path}\\${name}`, directory: false }));
      return { path, parent: path === 'C:\\Workspace\\my-project' ? 'C:\\Workspace' : 'C:\\Workspace\\my-project', entries: [...folders, ...files], truncated: false };
    }
    case 'createFile': {
      const name = args.path.split(/[\\/]/).at(-1);
      if (previewDocs[name] !== undefined) throw new Error('이미 같은 이름의 파일이 있습니다.');
      previewDocs[name] = '';
      return { path: args.path };
    }
    case 'read': {
      const name = args.path.split(/[\\/]/).at(-1);
      if (!(name in previewDocs)) throw new Error('데스크톱 앱에서 파일을 열어 주세요.');
      return { name, path: args.path, content: previewDocs[name], encoding: 'UTF-8', newline: 'LF', revision: 'preview' };
    }
    case 'save': previewDocs[args.path.split(/[\\/]/).at(-1)] = args.content; return { revision: 'preview' };
    case 'stat': return { exists: true, directory: false, path: args.path };
    case 'settings':
      previewSettings = args;
      try { localStorage.setItem(previewSettingsKey, JSON.stringify(previewSettings)); } catch { /* browser preview remains usable without storage */ }
      return null;
    case 'dirty': return null;
    case 'clipboard': return navigator.clipboard.writeText(args.text);
    case 'clipboardRead': return navigator.clipboard.readText();
    default: throw new Error('이 기능은 Orbit.exe에서 사용할 수 있습니다. 현재는 브라우저 UI 미리보기입니다.');
  }
}
