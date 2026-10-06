window.__panelResult = { done: false };
(async () => {
  try {
    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const file = __FILE__, other = __OTHER__;
    const current = () => window.orbitDiagnostics().sessions[0];
    const closeActive = async () => {
      document.querySelector('.editor-tab.active .tab-close').click();
      await wait(120);
    };
    const editorFocused = () => document.activeElement.closest('#editor-host');
    const before = current();
    if (!before?.pid) throw Error('test terminal did not start');
    // The remote harness pastes its ready marker safely without submitting it.
    document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true
    }));
    await wait(200);

    await window.orbitUiTest.openFile(file);
    await window.orbitUiTest.openFile(other);
    document.querySelector('.editor-tab:not(.active) .tab-close').click();
    await wait(120);
    if (!editorFocused() || !window.orbitDiagnostics().editor) throw Error('inactive close stole editor focus');
    await window.orbitUiTest.openFile(file);
    await closeActive();
    if (!editorFocused() || !window.orbitDiagnostics().editor) throw Error('remaining editor lost focus');
    window.orbitUiTest.editContent('unsaved fixture');
    document.querySelector('.editor-tab.active .tab-close').click();
    await wait(80);
    document.querySelector('#modal .dialog-actions .secondary').click();
    await wait(120);
    if (!editorFocused() || !window.orbitDiagnostics().editor) throw Error('canceled close lost editor focus');
    await window.orbitUiTest.save();
    await closeActive();

    const sizes = [], focus = [];
    for (let n = 0; n < 6; n++) {
      await window.orbitUiTest.openFile(file);
      await wait(120);
      sizes.push(current().cols);
      await closeActive();
      sizes.push(current().cols);
      focus.push(document.activeElement.className);
      if (!document.activeElement.classList.contains('xterm-helper-textarea')) {
        throw Error('terminal focus lost after file close: ' + document.activeElement.tagName);
      }
      // Send keyboard events to the actual focused element, through xterm's input handlers.
      // A bridge-level paste would succeed even with the focus bug and miss the regression.
      const marker = 'PANELINPUT' + n;
      for (const key of 'echo ' + marker) {
        const charCode = key.charCodeAt(0);
        document.activeElement.dispatchEvent(new KeyboardEvent('keypress', {
          key, keyCode: charCode, charCode, which: charCode, bubbles: true, cancelable: true
        }));
      }
      document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {
        key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true
      }));
      for (let i = 0; i < 100 && !current().output.split('\n').some(line => line.trim() === marker); i++) await wait(50);
      if (!current().output.split('\n').some(line => line.trim() === marker)) throw Error('terminal command output stopped');
    }
    const after = current();
    if (after.id !== before.id || after.pid !== before.pid || after.exited) throw Error('terminal replaced or exited');
    if (!sizes.some((width, index) => index % 2 === 0 && width < sizes[index + 1])) throw Error('panel did not resize terminal');
    window.__panelResult = { done: true, ok: true, pid: after.pid, sizes, focus, outputCount: after.outputCount };
  } catch (error) {
    window.__panelResult = { done: true, ok: false, error: String(error) };
  }
})();
