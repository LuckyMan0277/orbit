import test from 'node:test';
import assert from 'node:assert/strict';
import { createTerminalOutputReceiver } from '../src/terminal-output.js';

function fixture() {
  const writes = [], messages = [], counts = [];
  const receive = createTerminalOutputReceiver({
    write: (text, done) => writes.push({ text, done }),
    notify: (method, args) => messages.push({ method, ...args }),
    processed: () => {}, onOutput: length => counts.push(length)
  });
  const chunk = (seq, data = `chunk-${seq}`, session = 'a') => ({ session, seq, data, recoverable: true });
  return { receive, writes, messages, counts, chunk };
}

test('pending retries write once and cannot acknowledge unparsed output', () => {
  const f = fixture();
  for (let i = 0; i < 10000; i++) f.receive(f.chunk(1));
  assert.equal(f.writes.length, 1);
  assert.equal(f.counts.length, 1);
  assert.equal(f.messages.filter(x => x.method === 'ack').length, 0);
  f.writes[0].done();
  assert.deepEqual(f.messages.at(-1), { method: 'ack', session: 'a', seq: 1 });
});

test('lost final ACK recovers from completed duplicate without duplicate output', () => {
  const f = fixture();
  f.receive(f.chunk(1)); f.writes[0].done();
  f.messages.length = 0; // The first ACK did not reach the host.
  f.receive(f.chunk(1));
  assert.equal(f.writes.length, 1);
  assert.deepEqual(f.messages, [
    { method: 'outputReceived', session: 'a', seq: 1 },
    { method: 'ack', session: 'a', seq: 1 }
  ]);
  f.receive(f.chunk(2));
  f.receive(f.chunk(1)); // late duplicate of an older delivery
  assert.deepEqual(f.writes.map(x => x.text), ['chunk-1', 'chunk-2']);
  assert.equal(f.messages.filter(x => x.method === 'ack' && x.seq === 2).length, 0);
  f.writes[1].done();
});

test('deduplication belongs to each terminal, even with equal sequences', () => {
  const a = fixture(), b = fixture();
  a.receive(a.chunk(1)); b.receive(b.chunk(1, 'other', 'b'));
  a.writes[0].done(); b.writes[0].done();
  assert.equal(a.writes.length, 1); assert.equal(b.writes.length, 1);
  assert.equal(b.messages.at(-1).session, 'b');
});

test('write failure retries the same output; status callback failure cannot queue it twice', () => {
  let fail = true, done, writes = 0;
  const receive = createTerminalOutputReceiver({
    write: (_text, callback) => { if (fail) { fail = false; throw Error('write failed'); } writes++; done = callback; },
    notify: () => {}, processed: () => {}, onOutput: () => { throw Error('status failed'); }
  });
  const chunk = { session: 'a', seq: 1, data: 'preserved', recoverable: true };
  assert.throws(() => receive(chunk), /write failed/);
  assert.throws(() => receive(chunk), /status failed/);
  receive(chunk);
  assert.equal(writes, 1); done(); receive(chunk); assert.equal(writes, 1);
});

test('legacy nonsequenced and macOS output retains callback ACK behavior', () => {
  const f = fixture();
  f.receive({ session: 'a', data: 'legacy' });
  f.receive({ session: 'a', seq: 1, data: 'mac' });
  assert.equal(f.writes.length, 2);
  f.writes.forEach(x => x.done());
  assert.deepEqual(f.messages, [{ method: 'ack', session: 'a' }, { method: 'ack', session: 'a' }]);
});
