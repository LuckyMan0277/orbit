import test from 'node:test';
import assert from 'node:assert/strict';
import { extractLinks, parseLocation } from '../src/links.js';
test('paths retain drive, unicode, and line coordinates', () => {
  assert.deepEqual(parseLocation('C:\\작업\\main.cs:42:7'), { kind: 'path', path: 'C:\\작업\\main.cs', line: 42, column: 7 });
  assert.equal(extractLinks('수정: src/main.js:12')[0].value, 'src/main.js:12');
});
test('quoted spaces and markdown addresses have accurate offsets', () => {
  const text = 'open "C:\\my project\\hello.txt" and [docs](https://example.org/a).';
  const links = extractLinks(text);
  assert.equal(links[0].value, 'C:\\my project\\hello.txt');
  assert.equal(links[1].value, 'https://example.org/a');
  for (const link of links) assert.equal(text.slice(link.start, link.end), link.value);
});
test('URL ports are not interpreted as line numbers', () => {
  assert.deepEqual(parseLocation('http://localhost:3000'), { kind: 'url', path: 'http://localhost:3000' });
  assert.equal(parseLocation('file:///C:/my%20project/a.txt').path, 'C:/my project/a.txt');
});
test('Codex-style absolute paths normalize without losing coordinates', () => {
  assert.deepEqual(parseLocation('/C:/Users/dev/Desktop/TaskManager/orbit.exe:12:3'), { kind: 'path', path: 'C:/Users/dev/Desktop/TaskManager/orbit.exe', line: 12, column: 3 });
  assert.deepEqual(parseLocation('file:///C:/my%20project/orbit.exe'), { kind: 'path', path: 'C:/my project/orbit.exe', line: 1, column: 1 });
  assert.equal(parseLocation('file:///C:/my%2520project/orbit.exe').path, 'C:/my%20project/orbit.exe');
  assert.equal(parseLocation('reference%20note.txt').path, 'reference note.txt');
});
test('web addresses without a scheme open as URLs, files stay files', () => {
  const text = '서버 localhost:3000/app, www.example.co.kr 및 example.com/a?b=1 접속. 127.0.0.1:8080 mail a@b.com';
  const values = extractLinks(text).map(link => link.value);
  assert.deepEqual(values.slice(0, 4), ['localhost:3000/app', 'www.example.co.kr', 'example.com/a?b=1', '127.0.0.1:8080']);
  assert.ok(!values.includes('b.com'));
  assert.deepEqual(parseLocation('localhost:3000/app'), { kind: 'url', path: 'http://localhost:3000/app' });
  assert.deepEqual(parseLocation('127.0.0.1:8080'), { kind: 'url', path: 'http://127.0.0.1:8080' });
  assert.deepEqual(parseLocation('www.example.com'), { kind: 'url', path: 'https://www.example.com' });
  assert.deepEqual(parseLocation('example.dev/docs'), { kind: 'url', path: 'https://example.dev/docs' });
  for (const file of ['main.py', 'README.md', 'src/app.js:12', 'v1.2.3', 'C:\a\b.com']) assert.equal(parseLocation(file).kind, 'path', file);
  const text2 = 'edit main.py and README.md';
  for (const link of extractLinks(text2)) assert.equal(parseLocation(link.value).kind, 'path');
});
test('macOS-style paths keep their home marker and line coordinates', () => {
  const values = extractLinks('see ~/notes/todo.md and /Users/me/app/main.js:12:3').map(link => link.value);
  assert.deepEqual(values, ['~/notes/todo.md', '/Users/me/app/main.js:12:3']);
  assert.deepEqual(parseLocation('/Users/me/app/main.js:12:3'), { kind: 'path', path: '/Users/me/app/main.js', line: 12, column: 3 });
});
