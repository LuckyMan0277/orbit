// Keep terminal paste data separate from the submit key. Some CLIs interpret a
// carriage return in the same paste burst as pasted text instead of submission.
export function composePackets(text, bracketedPaste, submit) {
  let body = text.replace(/\r\n|\r|\n/g, '\r');
  if (bracketedPaste) body = `\u001b[200~${body}\u001b[201~`;
  return submit ? [body, '\r'] : [body];
}

export function composeStageKey(generation, session, text, submit) {
  return `${generation}\n${session}\n${text}\n${submit ? 'submit' : 'input'}`;
}

export function epochChanged(lastKnownEpoch, observedEpoch) {
  return !!lastKnownEpoch && !!observedEpoch && lastKnownEpoch !== observedEpoch;
}

// `stage` survives an uncertain submit receipt, so a retry verifies or sends
// only Enter after its already-accepted body.
export async function sendComposerPackets(stage, packets, submit, send) {
  if (!stage.bodySent) stage.bodySent = await send(packets[0]);
  return stage.bodySent && (!submit || await send(packets[1]));
}

// Paths of files sent from the phone, appended to the composer text. Paths with spaces are quoted so the AI reads them as one path.
export function attachmentText(current, paths) {
  const text = paths.map(path => /\s/.test(path) ? `"${path}"` : path).join(' ');
  return `${current}${current && !/\s$/.test(current) ? ' ' : ''}${text} `;
}
