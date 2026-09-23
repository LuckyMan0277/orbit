import test from 'node:test';
import assert from 'node:assert/strict';
import { attachmentText, composePackets, composeStageKey, epochChanged, sendComposerPackets } from '../src/remote-input.js';

test('send action sends paste body before a distinct submit Enter', () => {
  assert.deepEqual(composePackets('review\r\nthis', true, true), ['\u001b[200~review\rthis\u001b[201~', '\r']);
});

test('input-only keeps its body distinct and does not add Enter', () => {
  assert.deepEqual(composePackets('keep drafting', false, false), ['keep drafting']);
});

test('a retry after an indeterminate Enter never replays its accepted body', async () => {
  const stage = { bodySent:false }, packets = composePackets('one line', false, true), sent = [];
  assert.equal(await sendComposerPackets(stage, packets, true, async data => { sent.push(data); return data !== '\r'; }), false);
  assert.deepEqual(sent, ['one line', '\r']);
  assert.equal(await sendComposerPackets(stage, packets, true, async data => { sent.push(data); return true; }), true);
  assert.deepEqual(sent, ['one line', '\r', '\r']);
});

test('a new server epoch creates a fresh stage and sends its body again', async () => {
  const stages = new Map(), packets = composePackets('same draft', false, true);
  const oldKey = composeStageKey(3, 'codex', 'same draft', true);
  stages.set(oldKey, { bodySent:true });
  const newKey = composeStageKey(4, 'codex', 'same draft', true), stage = stages.get(newKey) || { bodySent:false }, sent = [];
  assert.notEqual(newKey, oldKey);
  assert.equal(await sendComposerPackets(stage, packets, true, async data => { sent.push(data); return true; }), true);
  assert.deepEqual(sent, ['same draft', '\r']);
});

test('a same-epoch reconnect retains the partial stage and retries only Enter', async () => {
  const stages = new Map(), key = composeStageKey(7, 'codex', 'same draft', true);
  stages.set(key, { bodySent:true });
  const sent = [];
  assert.equal(await sendComposerPackets(stages.get(composeStageKey(7, 'codex', 'same draft', true)), composePackets('same draft', false, true), true, async data => { sent.push(data); return true; }), true);
  assert.deepEqual(sent, ['\r']);
});

test('a soft reconnect keeps the last epoch so a new server invalidates its stage', () => {
  const lastKnownEpoch = 'epoch-before-disconnect'; // soft disconnect retains this value
  assert.equal(epochChanged(lastKnownEpoch, 'epoch-after-reconnect'), true);
  assert.equal(epochChanged(lastKnownEpoch, lastKnownEpoch), false);
});

test('attached file paths are appended to the draft, quoted when they contain spaces', () => {
  assert.equal(attachmentText('', ['C:\\p\\.orbit\\uploads\\a.jpg']), 'C:\\p\\.orbit\\uploads\\a.jpg ');
  assert.equal(attachmentText('look at', ['C:\\My Project\\b.png', 'C:\\p\\c.pdf']), 'look at "C:\\My Project\\b.png" C:\\p\\c.pdf ');
  assert.equal(attachmentText('look at ', ['C:\\p\\a.jpg']), 'look at C:\\p\\a.jpg ');
});
