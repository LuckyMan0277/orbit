import test from 'node:test';
import assert from 'node:assert/strict';
import { remainingDeadlineMs } from '../src/remote-connection.js';

test('initial connection shares one five-second deadline across sessions and snapshot', () => {
  const deadline = 5_000;
  assert.equal(remainingDeadlineMs(deadline, 0), 5_000);
  // A 4.9-second sessions response leaves only 100 ms for a hung snapshot.
  assert.equal(remainingDeadlineMs(deadline, 4_900), 100);
  assert.equal(remainingDeadlineMs(deadline, 5_000), 0);
});
