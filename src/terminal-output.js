// Deduplicate one outstanding native delivery without retaining terminal text.
// Legacy/macOS payloads have no recovery flag and keep their existing behavior.
export function createTerminalOutputReceiver({ write, notify, processed, onOutput }) {
  let completed = 0, pending = 0;
  return data => {
    const sequence = data.recoverable && Number.isSafeInteger(data.seq) && data.seq > 0 ? data.seq : 0;
    if (sequence) {
      notify('outputReceived', { session: data.session, seq: sequence });
      if (sequence <= completed) { notify('ack', { session: data.session, seq: sequence }); return; }
      if (sequence === pending) return;
      pending = sequence;
    }
    try {
      write(data.data, () => {
        if (sequence) { completed = sequence; pending = 0; }
        processed(data.seq);
        notify('ack', { session: data.session, ...(sequence ? { seq: sequence } : {}) });
      });
    } catch (error) { if (sequence) pending = 0; throw error; }
    onOutput(data.data.length);
  };
}
