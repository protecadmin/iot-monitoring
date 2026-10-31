const test = require('node:test');
const assert = require('node:assert/strict');

test('inclination keeps the four sensor decimals, including small negative tilt', async () => {
  const { formatMeasurement } = await import('../lib/format.ts');
  assert.equal(formatMeasurement(-16.5923, 'xDeg'), '-16.5923');
  assert.equal(formatMeasurement(-0.2516, 'yDeg'), '-0.2516');
  assert.equal(formatMeasurement(0, 'xDeg'), '0.0000');
});

test('ordinary measured fields retain two decimals and calculated precision stays adaptive', async () => {
  const { formatMeasurement } = await import('../lib/format.ts');
  assert.equal(formatMeasurement(31.2983, 'temperature'), '31.30');
  assert.equal(formatMeasurement(0.001234), '0.0012');
});
