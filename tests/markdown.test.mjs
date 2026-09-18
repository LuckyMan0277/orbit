import test from 'node:test';
import assert from 'node:assert/strict';
import { renderMarkdown } from '../src/markdown.js';

test('markdown preview renders common agent notes safely', () => {
  const html = renderMarkdown('# Plan\n\n| A | B |\n| - | - |\n| 1 | 2 |\n| 3 | 4 |\n\n1. one\n   - nested\n\n- [ ] todo\n- [x] done\n\n```js\nconst x = 1;\n```\n\n<script>alert(1)</script>\n[jump](javascript:alert(1))');
  assert.match(html, /<h1 id="plan">Plan<\/h1>/);
  assert.match(html, /<table>/);
  assert.equal((html.match(/<tr>/g) || []).length, 3);
  assert.match(html, /<ol>/);
  assert.match(html, /type="checkbox" disabled/);
  assert.match(html, /data-md-copy=/);
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /data-md-link="javascript:/i);
});

test('markdown keeps explicit local Windows links inert but clickable through Orbit', () => {
  const html = renderMarkdown('[exe](/C:/Tools/orbit.exe) [file](file:///C:/Tools/guide.pdf)');
  assert.match(html, /data-md-link="\/C:\/Tools\/orbit.exe"/);
  assert.match(html, /data-md-link="file:\/\/\/C:\/Tools\/guide.pdf"/);
});
