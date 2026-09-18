import { EditorView, minimalSetup } from 'codemirror';
import { Compartment, EditorState } from '@codemirror/state';
import { lineNumbers, highlightActiveLine, keymap } from '@codemirror/view';
import { indentWithTab } from '@codemirror/commands';
import { searchKeymap, highlightSelectionMatches } from '@codemirror/search';
import { StreamLanguage } from '@codemirror/language';

async function language(name) {
  const ext = name.split('.').at(-1).toLowerCase();
  if (['js','jsx','mjs','cjs','ts','tsx'].includes(ext)) return (await import('@codemirror/lang-javascript')).javascript({ typescript: ['ts','tsx'].includes(ext), jsx: ['jsx','tsx'].includes(ext) });
  if (ext === 'py') return (await import('@codemirror/lang-python')).python();
  if (['json','jsonc'].includes(ext)) return (await import('@codemirror/lang-json')).json();
  if (['md','markdown'].includes(ext)) return (await import('@codemirror/lang-markdown')).markdown();
  if (['html','htm','xml','svg'].includes(ext)) return (await import('@codemirror/lang-html')).html();
  if (['css','scss'].includes(ext)) return (await import('@codemirror/lang-css')).css();
  if (ext === 'sql') return (await import('@codemirror/lang-sql')).sql();
  if (['yaml','yml'].includes(ext)) return StreamLanguage.define((await import('@codemirror/legacy-modes/mode/yaml')).yaml);
  if (['sh','bash','zsh'].includes(ext)) return StreamLanguage.define((await import('@codemirror/legacy-modes/mode/shell')).shell);
  if (ext === 'ps1') return StreamLanguage.define((await import('@codemirror/legacy-modes/mode/powershell')).powerShell);
  if (['cs','c','cpp','h','java'].includes(ext)) { const modes = await import('@codemirror/legacy-modes/mode/clike'); return StreamLanguage.define(ext === 'cs' ? modes.csharp : ext === 'java' ? modes.java : modes.cpp); }
  if (ext === 'toml') return StreamLanguage.define((await import('@codemirror/legacy-modes/mode/toml')).toml);
  if (ext === 'rs') return (await import('@codemirror/legacy-modes/mode/rust')).rust ? StreamLanguage.define((await import('@codemirror/legacy-modes/mode/rust')).rust) : [];
  return [];
}
const themeSlot = new Compartment();
const editorTheme = name => EditorView.theme({
  '&': { height: '100%', color: name === 'light' ? '#30313b' : '#d5d9e2', backgroundColor: name === 'light' ? '#fcfbff' : '#15171c', fontSize: '13px' },
  '.cm-content': { fontFamily: '"Cascadia Mono", Consolas, "Malgun Gothic", monospace', padding: '18px 0', caretColor: name === 'light' ? '#50307c' : '#c1adff' },
  '.cm-scroller': { overflow: 'auto', lineHeight: '1.7' },
  '.cm-gutters': { backgroundColor: name === 'light' ? '#f5f1fb' : '#15171c', border: 'none', color: name === 'light' ? '#918aa2' : '#596170', paddingLeft: '12px', paddingRight: '10px' },
  '.cm-activeLine': { backgroundColor: name === 'light' ? '#eee8fa' : '#ffffff04' },
  '.cm-cursor': { borderLeftColor: name === 'light' ? '#50307c' : '#b8a1ff' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: name === 'light' ? '#cdbbe580' : '#74619b55' },
  '.cm-searchMatch': { backgroundColor: name === 'light' ? '#d8c477a8' : '#74619b88' },
  '.cm-panels': { backgroundColor: name === 'light' ? '#f5f1fb' : '#22252e', color: name === 'light' ? '#30313b' : '#d5d9e2' },
  '.cm-line': { padding: '0 16px 0 8px' }
}, { dark: name !== 'light' });
export async function createEditor(parent, file, changed, saved, cursor, themeName = 'dark') {
  const lang = await language(file.name);
  return new EditorView({ parent, state: EditorState.create({ doc: file.content, extensions: [minimalSetup, lineNumbers(), highlightActiveLine(), highlightSelectionMatches(), themeSlot.of(editorTheme(themeName)), lang, EditorState.lineSeparator.of(file.newline === 'CRLF' ? '\r\n' : '\n'), keymap.of([{ key: 'Mod-s', run: () => { saved(); return true; } }, indentWithTab, ...searchKeymap]), EditorView.updateListener.of(update => { if (update.docChanged) changed(); if (update.selectionSet || update.docChanged) { const line = update.state.doc.lineAt(update.state.selection.main.head); cursor(line.number, update.state.selection.main.head - line.from + 1); } })] }) });
}
export function updateTheme(view, name) { view.dispatch({ effects: themeSlot.reconfigure(editorTheme(name)) }); }
export function goTo(view, line = 1, column = 1) { const target = view.state.doc.line(Math.max(1, Math.min(line, view.state.doc.lines))); const anchor = Math.min(target.to, target.from + column - 1); view.dispatch({ selection: { anchor }, effects: EditorView.scrollIntoView(anchor, { y: 'center' }) }); view.focus(); }
