const test = require('node:test');
const assert = require('node:assert/strict');

test('tilt differences preserve signs, four decimals, missing axes, and remove negative zero', async () => {
  const { formatTiltNumber } = await import('../lib/format.ts');
  assert.equal(formatTiltNumber(1, true), '+1.0000');
  assert.equal(formatTiltNumber(-0.0023, true), '-0.0023');
  assert.equal(formatTiltNumber(null, true), '—');
  assert.equal(formatTiltNumber(-0.0000001, true), '0.0000');
  assert.equal(formatTiltNumber(-16.5923), '-16.5923');
});
