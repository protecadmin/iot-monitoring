const test = require('node:test');
const assert = require('node:assert/strict');

const packet = {
  v: 1, type: 'status', device_id: '860738070588839', timestamp_utc: 1793879303, uptime_s: 85108,
  firmware: { stm32: '0.5.15-STABLE', esp32_code: 638 },
  sensor: { name: 'BSWA308', connected: true },
  storage: { ready: true, used_records: 2128, capacity_records: 601739, write_errors: 0, verify_errors: 0, last_error: 0 },
  network: { configured_backhaul_mode: 'auto', uplink: '4g', sim_ready: true, registered: true, pdp: true, csq: 22, rssi_dbm: -69, mqtt: true, ip: '100.78.24.118', apn: 'jionet', rat: 'FDD LTE', band: 'LTE BAND 5', gnss: { fix: true, lat: 13.083180, lon: 80.220200, sats: 6, hdop: 4.09, age_s: 184 } },
  time: { valid: true, local_offset_min: 330 }, esp: { config_synced: true, free_heap: 148916 },
  errors: { sensor_rx_overflow: 0, modem_rx_overflow: 0, measurement_queue_rejections: 0 },
};

test('full reported status populates all eight cards and supersedes stale inventory', async () => {
  const { gatewayStatusPresentation } = await import('../lib/gateway-status.ts');
  const payload = JSON.stringify(packet);
  assert(Buffer.byteLength(payload) > 512);
  assert.throws(() => JSON.parse(payload.slice(0, 512)));
  const view = gatewayStatusPresentation(JSON.parse(payload), { firmwareVersion: 'old', rssi: -73, ipAddress: 'old' });
  assert.equal(view.firmware, '0.5.15-STABLE');
  assert.equal(view.firmwareNote, 'ESP32 Code 638');
  assert.equal(view.sensor, 'BSWA308');
  assert.equal(view.sensorNote, 'Connected');
  assert.equal(view.network, '4G • FDD LTE');
  assert.equal(view.networkNote, 'LTE BAND 5 · MQTT Connected');
  assert.equal(view.signal, '-69 dBm');
  assert.equal(view.signalNote, 'CSQ 22');
  assert.equal(view.ip, '100.78.24.118');
  assert.equal(view.uptime, '23h 38m 28s');
  assert.equal(view.storage, '2,128 / 601,739 records');
  assert.equal(view.storageNote, 'Ready · 0.35% used');
  assert.equal(view.gps, '13.083180° N / 80.220200° E');
  assert.equal(view.gpsNote, '6 satellites · HDOP 4.09 · Fix acquired');
  const diagnostics = Object.fromEntries(view.diagnostics);
  assert.equal(diagnostics['Storage write errors'], 0);
  assert.equal(diagnostics['Measurement queue rejections'], 0);
  assert.equal(diagnostics['ESP32 free heap (bytes)'], 148916);
  assert.equal(diagnostics['Local offset (min)'], 330);
});

test('missing status keeps inventory fallback without fabricating other card values', async () => {
  const { gatewayStatusPresentation } = await import('../lib/gateway-status.ts');
  const view = gatewayStatusPresentation({}, { firmwareVersion: 'existing', rssi: -73, ipAddress: '10.0.0.1' });
  assert.equal(view.signal, '-73 dBm');
  assert.equal(view.firmware, 'existing');
  assert.equal(view.ip, '10.0.0.1');
  for (const field of ['sensor', 'network', 'uptime', 'storage', 'gps']) assert.equal(view[field], undefined);
  assert.deepEqual(view.diagnostics, []);
});

test('false and zero status values remain visible and older GPS layout still works', async () => {
  const { gatewayStatusPresentation } = await import('../lib/gateway-status.ts');
  const view = gatewayStatusPresentation({ sensor: { connected: false }, uptime_s: 0, storage: { used_records: 0, capacity_records: 0, ready: false }, network: { rssi_dbm: 0, mqtt: false, sim_ready: false }, gnss: { fix: true, lat: -13, lon: -80, sats: 0 } }, { rssi: -73 });
  assert.equal(view.signal, '0 dBm');
  assert.equal(view.sensorNote, 'Disconnected');
  assert.equal(view.uptime, '0h 0m 0s');
  assert.equal(view.storage, '0 / 0 records');
  assert.equal(view.storageNote, 'Not ready');
  assert.equal(view.gps, '13.000000° S / 80.000000° W');
  assert.equal(Object.fromEntries(view.diagnostics)['SIM ready'], false);
});

test('latest status follows device timestamp even when packets arrive out of order', async () => {
  const { latestGatewayStatus } = await import('../lib/gateway-status.ts');
  const newest = { data: packet, message: { receivedAt: '2026-11-05T10:00:00Z' } };
  const delayed = { data: { ...packet, timestamp_utc: packet.timestamp_utc - 60 }, message: { receivedAt: '2026-11-05T10:01:00Z' } };
  const measurements = Array.from({ length: 30 }, () => ({ data: { type: 'measurement' }, message: { receivedAt: '2026-11-05T10:02:00Z' } }));
  assert.equal(latestGatewayStatus([delayed, newest]), newest);
  assert.equal(latestGatewayStatus([newest, ...measurements]), newest);
  assert.equal(latestGatewayStatus(measurements), undefined);
});
