export function paneIds(root, result = []) {
  if (!root) return result;
  if (root.type === 'pane') result.push(root.id);
  else { paneIds(root.first, result); paneIds(root.second, result); }
  return result;
}

export function splitPane(root, paneId, newPaneId, direction, splitId) {
  if (!root || !['row', 'column'].includes(direction) || paneIds(root).includes(newPaneId)) return root;
  let changed = false;
  const replace = node => {
    if (node.type === 'pane' && node.id === paneId) {
      changed = true;
      return { type: 'split', id: splitId, direction, ratio: 0.5, first: node, second: { type: 'pane', id: newPaneId } };
    }
    if (node.type === 'pane') return node;
    const first = replace(node.first), second = replace(node.second);
    return first === node.first && second === node.second ? node : { ...node, first, second };
  };
  const next = replace(root);
  return changed ? next : root;
}

export function removePane(root, paneId) {
  if (!root) return null;
  if (root.type === 'pane') return root.id === paneId ? null : root;
  const first = removePane(root.first, paneId), second = removePane(root.second, paneId);
  if (!first) return second;
  if (!second) return first;
  return first === root.first && second === root.second ? root : { ...root, first, second };
}

export function updateSplitRatio(root, splitId, ratio) {
  if (!root || root.type === 'pane') return root;
  const bounded = Math.max(0.15, Math.min(0.85, ratio));
  if (root.id === splitId) return { ...root, ratio: bounded };
  const first = updateSplitRatio(root.first, splitId, ratio), second = updateSplitRatio(root.second, splitId, ratio);
  return first === root.first && second === root.second ? root : { ...root, first, second };
}
