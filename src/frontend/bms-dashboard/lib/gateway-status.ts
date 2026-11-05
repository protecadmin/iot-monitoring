type Data = Record<string, unknown>;
export type GatewayStatusResponse = { data: Data | null; message: { receivedAt: string } };

function object(value: unknown): Data {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Data : {};
}
function text(value: unknown): string | undefined {
  return value === undefined || value === null || value === "" ? undefined : String(value);
}
function number(value: unknown): number | undefined {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}
const count = (value: unknown) => number(value)?.toLocaleString("en-US");

export function latestGatewayStatus<T extends GatewayStatusResponse>(responses: readonly T[]): T | undefined {
  return responses.filter(response => response.data?.type === "status").reduce<T | undefined>((latest, response) => {
    if (!latest) return response;
    const currentTime = number(response.data?.timestamp_utc) ?? Date.parse(response.message.receivedAt) / 1000;
    const latestTime = number(latest.data?.timestamp_utc) ?? Date.parse(latest.message.receivedAt) / 1000;
    return currentTime > latestTime ? response : latest;
  }, undefined);
}

export function gatewayStatusPresentation(status: Data, inventory: { firmwareVersion?: string | null; rssi?: number | null; ipAddress?: string | null }) {
  const firmware = object(status.firmware), sensor = object(status.sensor), network = object(status.network);
  const storage = object(status.storage), gps = object(network.gnss ?? status.gnss);
  const time = object(status.time), esp = object(status.esp), errors = object(status.errors);
  const rssi = number(network.rssi_dbm) ?? inventory.rssi;
  const uptime = number(status.uptime_s);
  const used = number(storage.used_records), capacity = number(storage.capacity_records);
  const latitude = number(gps.lat), longitude = number(gps.lon);
  const coordinate = (value: number, positive: string, negative: string) => `${Math.abs(value).toFixed(6)}° ${value >= 0 ? positive : negative}`;
  const diagnostics: [string, unknown][] = [
    ["SIM ready", network.sim_ready], ["Network registered", network.registered], ["PDP active", network.pdp], ["MQTT connected", network.mqtt],
    ["APN", network.apn], ["Radio technology", network.rat], ["LTE band", network.band],
    ["GPS fix age (s)", gps.age_s ?? gps.fix_age_s], ["Time valid", time.valid], ["Local offset (min)", time.local_offset_min],
    ["ESP32 config synced", esp.config_synced], ["ESP32 free heap (bytes)", esp.free_heap],
    ["Storage write errors", storage.write_errors], ["Storage verify errors", storage.verify_errors], ["Storage last error", storage.last_error],
    ["Sensor RX overflow", errors.sensor_rx_overflow], ["Modem RX overflow", errors.modem_rx_overflow], ["Measurement queue rejections", errors.measurement_queue_rejections],
  ];
  return {
    firmware: text(firmware.stm32) ?? text(inventory.firmwareVersion),
    firmwareNote: firmware.esp32_code == null ? undefined : `ESP32 Code ${firmware.esp32_code}`,
    sensor: text(sensor.name), sensorNote: sensor.connected == null ? undefined : sensor.connected ? "Connected" : "Disconnected",
    network: [text(network.uplink)?.toUpperCase(), text(network.rat)].filter(Boolean).join(" • ") || text(network.configured_backhaul_mode),
    networkNote: [text(network.band), network.mqtt == null ? undefined : network.mqtt ? "MQTT Connected" : "MQTT Disconnected"].filter(Boolean).join(" · "),
    signal: rssi == null ? undefined : `${rssi} dBm`, signalNote: network.csq == null ? undefined : `CSQ ${network.csq}`,
    ip: text(network.ip) ?? text(inventory.ipAddress),
    uptime: uptime === undefined ? undefined : `${Math.floor(uptime / 3600)}h ${Math.floor(uptime % 3600 / 60)}m ${Math.floor(uptime % 60)}s`,
    storage: used === undefined ? undefined : `${count(used)}${capacity === undefined ? "" : ` / ${count(capacity)}`} records`,
    storageNote: [storage.ready == null ? undefined : storage.ready ? "Ready" : "Not ready", used !== undefined && capacity !== undefined && capacity > 0 ? `${(used / capacity * 100).toFixed(2)}% used` : undefined].filter(Boolean).join(" · "),
    gps: gps.fix === true && latitude !== undefined && longitude !== undefined ? `${coordinate(latitude, "N", "S")} / ${coordinate(longitude, "E", "W")}` : gps.fix === false ? "No fix" : undefined,
    gpsNote: [gps.sats == null ? undefined : `${gps.sats} satellites`, gps.hdop == null ? undefined : `HDOP ${gps.hdop}`, gps.fix === true ? "Fix acquired" : undefined].filter(Boolean).join(" · "),
    diagnostics: diagnostics.filter(([, value]) => value !== null && value !== undefined),
  };
}
