import test from 'node:test';
import assert from 'node:assert/strict';
import { readableLines, conversationText, conversationDetails } from '../src/remote-readable.js';

const cell = (chars = '', code = 0, width = 1) => ({ getChars: () => chars, getCode: () => code, getWidth: () => width });
const padding = count => Array.from({ length: count }, () => cell());
const textCells = text => Array.from(text, char => cell(char, char.codePointAt(0)));
const line = (cells, isWrapped = false) => ({ length: cells.length, isWrapped, getCell: x => cells[x], translateToString: trim => cells.map(item => item.getChars()).join('').replace(trim ? /\s+$/ : /$/, '') });
const buffer = (lines, cursorY = 0) => ({ baseY: 0, cursorY, length: lines.length, getLine: y => lines[y] });

test('readable view drops right-side terminal padding before joining wrapped rows', () => {
  const rows = [
    line([...textCells('alpha'), ...padding(75)]),
    line([...padding(80)], true),
    line([...textCells('beta'), ...padding(76)], true),
  ];
  assert.equal(readableLines(buffer(rows, 2)), 'alphabeta');
});

test('readable view preserves explicit spaces used for indentation and within wrapped code', () => {
  const rows = [
    line([...textCells('    const value = '), cell(' ', 32), ...textCells('one'), ...padding(60)]),
    line([...textCells(' + two'), ...padding(74)], true),
  ];
  assert.equal(readableLines(buffer(rows, 1)), '    const value =  one + two');
});

test('readable view keeps cursor-positioned spaces inside the visible line', () => {
  const rows = [line([cell(), cell(), ...textCells('A'), cell(), ...textCells('B'), ...padding(74)])];
  assert.equal(readableLines(buffer(rows)), '  A B');
});

test('readable view removes only terminal trailing spaces at the logical line end', () => {
  const rows = [line([...textCells('result'), cell(' ', 32), cell(' ', 32), ...padding(73)])];
  assert.equal(readableLines(buffer(rows)), 'result');
});

test('long Korean and ASCII output retains every glyph across wrapped rows', () => {
  const korean = '한글가나';
  const rows = [
    line([cell('한', '한'.codePointAt(0), 2), cell('', 0, 0), ...textCells('글가나'), ...padding(75)]),
    line([...textCells(' ASCII https://example.test/path'), ...padding(49)], true),
  ];
  assert.equal(readableLines(buffer(rows, 1)), korean + ' ASCII https://example.test/path');
});

test('formatter never mutates the raw terminal cells', () => {
  const raw = [...textCells('raw'), ...padding(3)];
  const snapshot = raw.map(item => [item.getChars(), item.getCode(), item.getWidth()]);
  readableLines(buffer([line(raw)]));
  assert.deepEqual(raw.map(item => [item.getChars(), item.getCode(), item.getWidth()]), snapshot);
});

test('conversation view removes a real-shaped Codex footer and extracts its observed model', () => {
  const rows = [
    line([...textCells('OpenAI Codex')]),
    line([...textCells('model: gpt-5.6')]),
    line([...textCells('status: ready')]),
    line([...textCells('Approve this command? [y/N]')]),
    line([...textCells('----------------')]),
    line([...textCells('gpt-5.6-codex')]),
    line([...textCells('92% context left')]),
    line([...textCells('› ')]),
  ];
  assert.deepEqual(conversationDetails(buffer(rows, 7), 'codex'), { text:'model: gpt-5.6\nstatus: ready\nApprove this command? [y/N]', model:'gpt-5.6-codex' });
});

test('a lone prompt-looking line remains ordinary output', () => {
  const rows = [line([...textCells('result › ')]), line([...textCells('› ')]), line([...textCells('keep this')])];
  assert.equal(conversationText(buffer(rows, 2), 'claude'), 'result ›\n›\nkeep this');
});

test('Claude live footer is removed only with supporting footer context', () => {
  const rows = [
    line([...textCells('Keep this approval prompt: Allow?')]),
    line([...textCells('----------------')]),
    line([...textCells('claude-sonnet-4.5')]),
    line([...textCells('64% context remaining')]),
    line([...textCells('❯ ')]),
  ];
  assert.deepEqual(conversationDetails(buffer(rows, 4), 'claude'), { text:'Keep this approval prompt: Allow?', model:'claude-sonnet-4.5' });
});

test('a task model line before a footer boundary is never stripped', () => {
  const rows = ['model: gpt-5.6','Approve? [y/N]','----------------','gpt-5.6-codex','92% context left','› '].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(rows, 5), 'codex'), { text:'model: gpt-5.6\nApprove? [y/N]', model:'gpt-5.6-codex' });
});

test('two-divider Claude composer footer is removed without removing its answer', () => {
  const rows=['answer','────────────','❯ ','────────────','  ? for shortcuts'].map(text=>line([...textCells(text)]));
  assert.equal(conversationText(buffer(rows,4),'claude'),'answer');
});

test('combined Codex model context footer is removed but a nonempty draft stays', () => {
  const footer=['answer','────────────','› ','  gpt-5.6-codex high · 92% left · C:\\work'].map(text=>line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(footer,3),'codex'),{text:'answer',model:'gpt-5.6-codex'});
  const draft=['answer','────────────','› do not send this'].map(text=>line([...textCells(text)]));
  assert.equal(conversationText(buffer(draft,2),'codex'),'answer\n────────────\n› do not send this');
});

test('conversation view never strips arbitrary task output or shell output', () => {
  const rows = [line([...textCells('model: gpt-5.6')]), line([...textCells('status: ready')])];
  assert.equal(conversationText(buffer(rows, 1), 'codex'), 'model: gpt-5.6\nstatus: ready');
  assert.equal(conversationText(buffer(rows, 1), 'powershell'), 'model: gpt-5.6\nstatus: ready');
});

test('Codex exact composer placeholder is removed only from a supported live footer', () => {
  const footer = ['answer','────────────','› Ask Codex to do anything','────────────','  gpt-5.6-codex high · 92% left'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(footer, 4), 'codex'), { text:'answer', model:'gpt-5.6-codex' });
  const alternatePrompt = ['answer','────────────','> ask codex to do anything','  92% context left'].map(text => line([...textCells(text)]));
  assert.equal(conversationText(buffer(alternatePrompt, 3), 'codex'), 'answer');
});

test('Codex worked-for status block is removed only as the exact tail layout', () => {
  const rows = [
    'Finished task output.',
    'Approve this command? [y/N]',
    '• Worked for 2m 14s',
    '────────────────────',
    '› Ask Codex to do anything',
    '────────────────────',
    '? for shortcuts',
    'gpt-5.6-sol low · 82% left · C:\\work',
  ].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), {
    text: 'Finished task output.\nApprove this command? [y/N]', model: 'gpt-5.6-sol',
  });
});

test('Codex worked-for status block accepts CLI timer case and spacing variants', () => {
  for (const timer of ['WORKED FOR 8S', 'Worked   for   1m 02s', 'Worked for 1h 3m']) {
    const rows = ['answer', timer, '────────────', '> ask codex to do anything', '────────────', 'gpt-5.6-terra HIGH · 45% context remaining'].map(text => line([...textCells(text)]));
    assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-terra' });
  }
});

test('Codex worked-for status block removes an empty live composer prompt', () => {
  for (const [prompt, shortcut] of [['› ', '? for shortcuts'], ['❯ ', '? for help']]) {
    const rows = ['answer', '• Worked for 9s', '────────────', prompt, '────────────', shortcut, 'gpt-5.6-sol low · 82% left · C:\\work'].map(text => line([...textCells(text)]));
    assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
  }
});

test('Codex worked-for status block removes its Main default branch label', () => {
  const rows = ['answer', 'Worked for 2m 14s', '────────────', '› Ask Codex to do anything', '────────────', 'Main[default]', 'gpt-5.6-sol low · 82% left · C:\\work'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
  const combined = ['answer', 'Worked for 9s', '────────────', '> Ask Codex to do anything Main [ default ]', '────────────', 'gpt-5.6-sol low · 82% left'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(combined, combined.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
});

test('Codex tail fallback removes split composer chrome without a worked timer', () => {
  const rows = ['answer', '────────────', '› Ask Codex to do anything', '────────────', 'Main[default]', 'gpt-5.6-sol low · 82% left · C:\\work'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
  const wrapped = ['answer', '>   Ask Codex to do anything     Main [ default ]', 'gpt-5.6-sol low · 82% left'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(wrapped, wrapped.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
  const branchOnly = ['answer', 'Main [ default ]', 'gpt-5.6-sol low · 82% left'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(branchOnly, branchOnly.length - 1), 'codex'), { text: 'answer', model: 'gpt-5.6-sol' });
});

test('Codex tail fallback removes the live model cwd title and branch footer', () => {
  const rows = ['assistant answer', '› Ask Codex to do anything', 'gpt-5.6-luna medium · ~\\Desktop\\TaskManager · 모바일 입력창과 모델명 개선 · Main [default]'].map(text => line([...textCells(text)]));
  assert.deepEqual(conversationDetails(buffer(rows, rows.length - 1), 'codex'), { text: 'assistant answer', model: 'gpt-5.6-luna' });
  const standalone = ['gpt-5.6-luna medium · ~\\Desktop\\TaskManager · 모바일 입력창과 모델명 개선 · Main [default]'].map(text => line([...textCells(text)]));
  assert.equal(conversationText(buffer(standalone, 0), 'codex'), 'gpt-5.6-luna medium · ~\\Desktop\\TaskManager · 모바일 입력창과 모델명 개선 · Main [default]');
});

test('Codex status matcher retains standalone, quoted, code, draft, and incomplete timer layouts', () => {
  const cases = [
    ['Worked for 2m 14s'],
    ['• Worked for 9s'],
    ['The report says: Worked for 2m 14s', 'gpt-5.6-sol low · 82% context left'],
    ['console.log("Worked for 2m 14s")', 'gpt-5.6-sol low · 82% context left'],
    ['Worked for 2m 14s', '────────────', '› Ask Codex to do anything', '────────────', '› a real user draft'],
    ['Worked for 2m 14s', '────────────', '› Ask Codex to do anything', '────────────', 'gpt-5.6-sol low · 82% context left', 'task output after footer'],
  ];
  for (const source of cases) {
    const rows = source.map(text => line([...textCells(text)]));
    assert.equal(conversationText(buffer(rows, rows.length - 1), 'codex'), source.join('\n'));
  }
});

test('Codex status parsing never deletes a response between separate chrome regions', () => {
  const source = ['Worked for 2m 14s', 'assistant response that must remain', '────────────', '› Ask Codex to do anything', '────────────', 'gpt-5.6-sol low · 82% context left'];
  const rows = source.map(text => line([...textCells(text)]));
  assert.equal(conversationText(buffer(rows, rows.length - 1), 'codex'), 'assistant response that must remain');
});

test('Codex status parsing leaves raw readable lines unchanged', () => {
  const source = ['Worked for 9s', '────────────', '› Ask Codex to do anything', '────────────', 'gpt-5.6-sol low · 82% context left'];
  const rows = source.map(text => line([...textCells(text)]));
  assert.equal(readableLines(buffer(rows, rows.length - 1)), source.join('\n'));
  assert.equal(conversationText(buffer(rows, rows.length - 1), 'codex'), '');
});

test('Codex placeholder words in drafts, messages, code, and approvals remain transcript content', () => {
  const cases = [
    ['> Ask Codex to do anything'],
    ['User wrote: Ask Codex to do anything'],
    ['console.log("Ask Codex to do anything")'],
    ['Approve this command? Ask Codex to do anything'],
  ];
  for (const rows of cases) assert.equal(conversationText(buffer(rows.map(text => line([...textCells(text)])), rows.length - 1), 'codex'), rows.join('\n'));
});
