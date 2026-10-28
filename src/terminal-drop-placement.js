const edgeThreshold = 0.22;

export function getDropPlacement(bounds, point, headerHeight = 30) {
  const bodyTop = bounds.top + headerHeight;
  const bodyHeight = Math.max(1, bounds.height - headerHeight);
  if (point.y < bodyTop) return 'center';
  const x = Math.max(0, Math.min(1, (point.x - bounds.left) / Math.max(1, bounds.width)));
  const y = Math.max(0, Math.min(1, (point.y - bodyTop) / bodyHeight));
  const edge = edgeThreshold;
  if (x < edge && y < edge) return 'top-left';
  if (x > 1-edge && y < edge) return 'top-right';
  if (x < edge && y > 1-edge) return 'bottom-left';
  if (x > 1-edge && y > 1-edge) return 'bottom-right';
  if (x < edge) return 'left';
  if (x > 1-edge) return 'right';
  if (y < edge) return 'top';
  if (y > 1-edge) return 'bottom';
  return 'center';
}
