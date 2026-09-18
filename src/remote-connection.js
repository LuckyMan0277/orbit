export function remainingDeadlineMs(deadline, now = Date.now()) {
  return Math.max(0, deadline - now);
}
