// Gives the UI the same `window.chrome.webview` surface it has inside WebView2 on Windows
// (src/bridge.js), backed by Tauri. Runs before any page script.
(() => {
  if (window.chrome && window.chrome.webview) return;
  const listeners = new Set();
  const emit = data => {
    for (const listener of [...listeners]) {
      try { listener({ data }); } catch (error) { console.error(error); }
    }
  };
  // Input, acknowledgements and resizes must reach the host in the order they were sent; the
  // synchronous command runs in arrival order, so those go through it. It only queues work.
  const ordered = new Set(['write', 'ack', 'resize', 'dirty', 'theme', 'windowMinimize', 'windowMaximize', 'windowClose']);
  window.__orbitEvent = emit;
  window.chrome = Object.assign(window.chrome || {}, {
    webview: {
      addEventListener(type, listener) { if (type === 'message') listeners.add(listener); },
      removeEventListener(type, listener) { listeners.delete(listener); },
      postMessage(message) {
        const id = message && message.id;
        const command = ordered.has(message && message.method) ? 'orbit_ordered' : 'orbit_rpc';
        Promise.resolve()
          .then(() => window.__TAURI_INTERNALS__.invoke(command, { msg: message }))
          .then(
            result => { if (id) emit({ id, result }); },
            error => {
              const text = typeof error === 'string' ? error : (error && error.message) || String(error);
              if (id) emit({ id, error: text }); else emit({ type: 'error', message: text });
            });
      }
    }
  });
})();
