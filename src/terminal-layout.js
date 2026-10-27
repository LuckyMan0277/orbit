export function hasTerminalViewport(width, height) {
  return Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0;
}

export function getChangedTerminalSize(previous, current) {
  if (!current || current.cols < 1 || current.rows < 1) return null;
  return previous?.cols === current.cols && previous?.rows === current.rows
    ? null
    : { cols: current.cols, rows: current.rows };
}

export class TerminalLayout {
  constructor({ element, fit, term, ResizeObserverImpl = globalThis.ResizeObserver, requestFrame = callback => globalThis.requestAnimationFrame(callback), cancelFrame = id => globalThis.cancelAnimationFrame(id), fontsReady = globalThis.document?.fonts?.ready }) {
    this.element = element;
    this.fit = fit;
    this.term = term;
    this.requestFrame = requestFrame;
    this.cancelFrame = cancelFrame;
    this.frame = null;
    this.disposed = false;
    this.fontsLoaded = false;
    this.readyPromise = new Promise(resolve => { this.resolveReady = resolve; });
    this.observer = new ResizeObserverImpl(() => this.schedule());
    this.observer.observe(element);
    Promise.resolve(fontsReady).catch(() => {}).then(() => {
      if (this.disposed) return;
      this.fontsLoaded = true;
      this.schedule();
    });
  }

  schedule() {
    if (this.disposed || !this.fontsLoaded || this.frame !== null) return;
    this.frame = this.requestFrame(() => {
      this.frame = null;
      if (this.disposed || !hasTerminalViewport(this.element.clientWidth, this.element.clientHeight)) return;
      const proposed = this.fit.proposeDimensions();
      if (!proposed || proposed.cols < 1 || proposed.rows < 1) return;
      this.fit.fit();
      if (this.term.cols === proposed.cols && this.term.rows === proposed.rows) this.resolveReady(true);
    });
  }

  ready() { return this.readyPromise; }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.observer.disconnect();
    if (this.frame !== null) this.cancelFrame(this.frame);
    this.frame = null;
    this.resolveReady(false);
  }
}
