const group = (id, tabId) => ({ type: 'group', id, tabs: tabId ? [tabId] : [], activeTabId: tabId || '' });

export function groupIds(root, result = []) {
  if (!root) return result;
  if (root.type === 'group') result.push(root.id);
  else { groupIds(root.first, result); groupIds(root.second, result); }
  return result;
}

export function tabIds(root, result = []) {
  if (!root) return result;
  if (root.type === 'group') result.push(...root.tabs);
  else { tabIds(root.first, result); tabIds(root.second, result); }
  return result;
}

export function findGroup(root, id) {
  if (!root || root.type === 'group') return root?.id === id ? root : null;
  return findGroup(root.first, id) || findGroup(root.second, id);
}

function replaceGroup(root, id, update) {
  if (!root) return root;
  if (root.type === 'group') return root.id === id ? update(root) : root;
  const first = replaceGroup(root.first, id, update), second = replaceGroup(root.second, id, update);
  return first === root.first && second === root.second ? root : { ...root, first, second };
}

export function addTab(root, groupId, tabId) {
  if (!root || !tabId || tabIds(root).includes(tabId)) return root;
  return replaceGroup(root, groupId, item => ({ ...item, tabs: [...item.tabs, tabId], activeTabId: tabId }));
}

export function activateTab(root, groupId, tabId) {
  return replaceGroup(root, groupId, item => item.tabs.includes(tabId) && item.activeTabId !== tabId ? { ...item, activeTabId: tabId } : item);
}

export function reorderTab(root, groupId, tabId, index) {
  return replaceGroup(root, groupId, item => {
    const from = item.tabs.indexOf(tabId);
    if (from < 0) return item;
    const tabs = [...item.tabs]; tabs.splice(from, 1);
    const to = Math.max(0, Math.min(tabs.length, index));
    if (from === to) return item;
    tabs.splice(to, 0, tabId);
    return { ...item, tabs };
  });
}

export function removeTab(root, tabId) {
  if (!root) return null;
  if (root.type === 'group') {
    if (!root.tabs.includes(tabId)) return root;
    const tabs = root.tabs.filter(id => id !== tabId);
    if (!tabs.length) return null;
    return { ...root, tabs, activeTabId: root.activeTabId === tabId ? tabs[Math.min(root.tabs.indexOf(tabId), tabs.length - 1)] : root.activeTabId };
  }
  const first = removeTab(root.first, tabId), second = removeTab(root.second, tabId);
  if (!first) return second;
  if (!second) return first;
  return first === root.first && second === root.second ? root : { ...root, first, second };
}

export function splitGroup(root, groupId, newGroupId, tabId, direction, splitId, side = 'after') {
  if (!root || !['row', 'column'].includes(direction) || groupIds(root).includes(newGroupId) || tabIds(root).includes(tabId)) return root;
  let changed = false;
  const replace = node => {
    if (node.type === 'group' && node.id === groupId) {
      changed = true;
      const next = group(newGroupId, tabId);
      return { type: 'split', id: splitId, direction, ratio: 0.5, first: side === 'before' ? next : node, second: side === 'before' ? node : next };
    }
    if (node.type === 'group') return node;
    const first = replace(node.first), second = replace(node.second);
    return first === node.first && second === node.second ? node : { ...node, first, second };
  };
  const next = replace(root);
  return changed ? next : root;
}

export function dockTab(root, sourceGroupId, tabId, targetGroupId, placement, ids, targetIndex) {
  const source = findGroup(root, sourceGroupId), target = findGroup(root, targetGroupId);
  if (!source || !target || !source.tabs.includes(tabId)) return root;
  if (placement === 'center') {
    if (sourceGroupId === targetGroupId) return root;
    const removed = removeTab(root, tabId);
    const merged = addTab(removed, targetGroupId, tabId);
    return Number.isInteger(targetIndex) ? reorderTab(merged, targetGroupId, tabId, targetIndex) : merged;
  }
  const directions = { left: ['row', 'before'], right: ['row', 'after'], top: ['column', 'before'], bottom: ['column', 'after'] };
  const dock = directions[placement];
  if (!dock || sourceGroupId === targetGroupId && source.tabs.length === 1) return root;
  const removed = removeTab(root, tabId);
  const survivingTarget = findGroup(removed, targetGroupId);
  if (!survivingTarget) return root;
  return splitGroup(removed, targetGroupId, ids.groupId, tabId, dock[0], ids.splitId, dock[1]);
}

export function splitCorner(root, sourceGroupId, targetGroupId, tabId, corner, ids) {
  const source = findGroup(root, sourceGroupId);
  const target = findGroup(root, targetGroupId);
  if (!source || !source.tabs.includes(tabId) || !target || !['top-left', 'top-right', 'bottom-left', 'bottom-right'].includes(corner)) return root;
  if (sourceGroupId === targetGroupId && source.tabs.length === 1) return root;
  const emptyId = ids.emptyGroupId, innerId = ids.innerSplitId;
  const horizontalSide = corner.endsWith('left') ? 'left' : 'right';
  const verticalSide = corner.startsWith('top') ? 'top' : 'bottom';
  const removed = removeTab(root, tabId);
  const remainingTarget = findGroup(removed, targetGroupId);
  if (!remainingTarget) return root;
  const horizontal = splitGroup(removed, targetGroupId, emptyId, '', 'row', innerId, horizontalSide === 'left' ? 'before' : 'after');
  const vertical = splitGroup(horizontal, emptyId, ids.tabGroupId, tabId, 'column', ids.outerSplitId, verticalSide === 'top' ? 'before' : 'after');
  return vertical;
}

export function updateTabSplitRatio(root, splitId, ratio) {
  if (!root || root.type === 'group') return root;
  const bounded = Math.max(0.15, Math.min(0.85, ratio));
  if (root.id === splitId) return { ...root, ratio: bounded };
  const first = updateTabSplitRatio(root.first, splitId, ratio), second = updateTabSplitRatio(root.second, splitId, ratio);
  return first === root.first && second === root.second ? root : { ...root, first, second };
}
