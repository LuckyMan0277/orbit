import test from 'node:test';
import assert from 'node:assert/strict';
import { getChangedTerminalSize, hasTerminalViewport, TerminalLayout } from '../src/terminal-layout.js';

class FrameQueue {
  #next = 1;
  #callbacks = new Map();
  request = callback => { const id = this.#next++; this.#callbacks.set(id, callback); return id; };
  cancel = id => this.#callbacks.delete(id);
  flush() { const callbacks = [...this.#callbacks.values()]; this.#callbacks.clear(); callbacks.forEach(callback => callback()); }
  get size() { return this.#callbacks.size; }
}

class ResizeObserverStub {
  constructor(callback) { this.callback = callback; ResizeObserverStub.last = this; this.disconnected = false; }
  observe() {}
  disconnect() { this.disconnected = true; }
  trigger() { this.callback(); }
}

test('managed fit waits for fonts and a nonzero viewport, then coalesces measured resizes', async () => {
  let resolveFonts;
  const fontsReady = new Promise(resolve => { resolveFonts = resolve; });
  const frames = new FrameQueue();
  const element = { clientWidth: 0, clientHeight: 0 };
  const term = { cols: 80, rows: 24 };
  let fits = 0;
  const layout = new TerminalLayout({
    element, term, ResizeObserverImpl: ResizeObserverStub, fontsReady,
    requestFrame: frames.request, cancelFrame: frames.cancel,
    fit: {
      proposeDimensions() { return { cols: Math.floor(element.clientWidth / 10), rows: Math.floor(element.clientHeight / 20) }; },
      fit() { fits++; term.cols = Math.floor(element.clientWidth / 10); term.rows = Math.floor(element.clientHeight / 20); },
    },
  });

  resolveFonts();
  await new Promise(resolve => setImmediate(resolve));
  frames.flush();
  assert.equal(fits, 0, 'zero-size startup must not fit');

  element.clientWidth = 800;
  element.clientHeight = 500;
  ResizeObserverStub.last.trigger();
  ResizeObserverStub.last.trigger();
  assert.equal(frames.size, 1, 'multiple observer notifications share one animation frame');
  frames.flush();
  assert.equal(await layout.ready(), true);
  assert.deepEqual([term.cols, term.rows], [80, 25]);
  assert.equal(fits, 1);

  element.clientWidth = 0;
  element.clientHeight = 0;
  ResizeObserverStub.last.trigger();
  frames.flush();
  assert.equal(fits, 1, 'minimized or hidden zero-size changes do not fit');

  element.clientWidth = 1000;
  element.clientHeight = 600;
  ResizeObserverStub.last.trigger();
  frames.flush();
  assert.deepEqual([term.cols, term.rows], [100, 30]);
  assert.equal(fits, 2);
  layout.dispose();
});

test('disposing layout cancels scheduled work and disconnects the observer', () => {
  const frames = new FrameQueue();
  let fits = 0;
  const layout = new TerminalLayout({
    element: { clientWidth: 800, clientHeight: 500 }, term: { cols: 80, rows: 24 },
    ResizeObserverImpl: ResizeObserverStub, fontsReady: Promise.resolve(),
    requestFrame: frames.request, cancelFrame: frames.cancel,
    fit: { proposeDimensions: () => ({ cols: 80, rows: 24 }), fit() { fits++; } },
  });
  layout.dispose();
  frames.flush();
  ResizeObserverStub.last.trigger();
  assert.equal(frames.size, 0);
  assert.equal(fits, 0);
  assert.equal(ResizeObserverStub.last.disconnected, true);
  return layout.ready().then(ready => assert.equal(ready, false));
});

test('terminal resize messages use the newest grid after startup races a resize', () => {
  const initial = { cols: 80, rows: 24 };
  assert.equal(getChangedTerminalSize(initial, { cols: 80, rows: 24 }), null);
  assert.deepEqual(getChangedTerminalSize(initial, { cols: 121, rows: 38 }), { cols: 121, rows: 38 });
  assert.equal(getChangedTerminalSize(initial, { cols: 0, rows: 38 }), null);
});

test('viewport dimensions must both be finite and positive', () => {
  assert.equal(hasTerminalViewport(1, 1), true);
  for (const box of [[0, 1], [1, 0], [-1, 1], [1, NaN], [Infinity, 1]]) assert.equal(hasTerminalViewport(...box), false);
});
