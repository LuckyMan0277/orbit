// Session list logic, kept free of the DOM so it can be tested. The window is organised around running agent sessions; each one belongs
// to the project it was opened in.

// What a session is doing, judged from its output only (no hooks needed): output flowing = working; a hidden session that produced output
// and went quiet wants a look; a pending API key request always does.
export function sessionStatus({ exited, asking, now, lastOutputAt, unread }, quietMs = 2500) {
  if (exited) return { key: 'ended', label: '끝남' };
  if (asking) return { key: 'attention', label: '키 요청' };
  if (lastOutputAt && now - lastOutputAt < quietMs) return { key: 'working', label: '작업 중' };
  if (unread) return { key: 'attention', label: '확인해 보세요' };
  return { key: 'idle', label: '대기' };
}

// Items grouped by project, in the order the projects first appear. Paths differing only in case are the same project.
export function groupByProject(items, projectOf) {
  const groups = new Map();
  for (const item of items) {
    const path = projectOf(item) || '', key = path.toLowerCase();
    if (!groups.has(key)) groups.set(key, { path, items: [] });
    groups.get(key).items.push(item);
  }
  return [...groups.values()];
}

// "orbit · Claude", then "orbit · Claude 2" when the project already has one: two projects can each have a Claude without the tabs looking alike.
export function sessionName(projectLabel, agentName, existing) {
  const base = projectLabel ? `${projectLabel} · ${agentName}` : agentName;
  return existing ? `${base} ${existing + 1}` : base;
}
// The same without the project, for rows that already sit under a project heading.
export function sessionLabel(agentName, existing) { return existing ? `${agentName} ${existing + 1}` : agentName; }
