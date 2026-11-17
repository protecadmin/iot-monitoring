import { Modal } from "@/components/ui/Modal";
import { tupleKey, type HubSnapshot, type SensorTuple } from "@/lib/hubs";

export const readOnly = (op: string) => ["get", "status.get", "api.test"].includes(op);
export const operations = ["get", "status.get", "site.set", "tv.select", "api.test", "api.set", "api.refresh", "display.set", "threshold.set"];
export const labels: Record<string, string> = { get: "Get Snapshot", "status.get": "Get Status", "site.set": "Site Settings", "tv.select": "TV Sensors", "api.test": "Test API", "api.set": "API Settings", "api.refresh": "Poll Sources", "display.set": "Display Timing", "threshold.set": "Alert Limits" };
export function defaultData(op: string, s: HubSnapshot | null): Record<string, unknown> {
  switch (op) {
    case "site.set": return { name: s?.site?.name ?? "", location: s?.site?.location ?? "", timezone: s?.site?.timezone ?? "" };
    case "tv.select": return { sensors: s?.tv?.selected_sensors ?? [] };
    case "api.test": return { url: s?.api?.url ?? "" };
    case "api.set": return { url: s?.api?.url ?? "", interval_s: s?.api?.interval_s ?? 30 };
    case "display.set": return { welcome_seconds: s?.tv?.welcome_seconds ?? 7, rotation_seconds: s?.tv?.rotation_seconds ?? 15 };
    case "threshold.set": return { api_alert_limits: s?.thresholds?.api_alert_limits ?? { noise: 85, ppv: 0.5, wbgt_warning: 30, wbgt: 33 } };
    default: return {};
  }
}
export function CommandParameters({ op, data, setData, version, snapshot, snapshotAge, onClose }: {
  op: string; data: Record<string, unknown>; setData: (data: Record<string, unknown>) => void;
  version?: number; snapshot: HubSnapshot | null; snapshotAge: number | null; onClose: () => void;
}) {
  return <Modal title={`${labels[op]} · parameters`} onClose={onClose} size="lg"><form className="mqtt-form" onSubmit={e => { e.preventDefault(); onClose(); }}>
    <p>Values are based on snapshot version {version ?? "unavailable"}. Review before sending.</p>
    {op === "tv.select" ? <div className="mqtt-selection">{snapshot?.devices?.map((sensor, index) => {
      const sensors = (data.sensors ?? []) as SensorTuple[]; const key = tupleKey(sensor);
      const stale = sensor.age_seconds == null || sensor.age_seconds + (snapshotAge ?? 0) > 18000;
      const ambiguous = snapshot.devices!.filter(s => tupleKey(s) === key).length !== 1;
      return <label key={`${key}-${index}`}><input type="checkbox" checked={sensors.some(s => tupleKey(s) === key)} disabled={stale || ambiguous} onChange={e => setData({ sensors: e.target.checked ? [...sensors, { source: sensor.source, site: sensor.site, sensor: sensor.sensor }] : sensors.filter(s => tupleKey(s) !== key) })} /><span><strong>{sensor.site} · {sensor.sensor}</strong><small>{sensor.source} · {sensor.state ?? "Unknown"}{stale ? " · Over 5h old or age unknown" : ""}{ambiguous ? " · Ambiguous identity" : ""}</small></span></label>;
    }) ?? <p>No inventory available.</p>}</div> : op === "threshold.set" ? Object.entries(data.api_alert_limits as Record<string, number>).map(([key, value]) => <label key={key}>{key.replaceAll("_", " ")}<input type="number" step="any" min="0" required value={value} onChange={e => setData({ api_alert_limits: { ...(data.api_alert_limits as Record<string, number>), [key]: Number(e.target.value) } })} /></label>) : Object.entries(data).map(([key, value]) => <label key={key}>{key.replaceAll("_", " ")}<input required={key !== "location"} type={typeof value === "number" ? "number" : key === "url" ? "url" : "text"} min={key === "welcome_seconds" ? 0 : 5} max={key === "welcome_seconds" ? 60 : key === "rotation_seconds" ? 180 : key === "interval_s" ? 3600 : undefined} value={String(value)} onChange={e => setData({ ...data, [key]: typeof value === "number" ? Number(e.target.value) : e.target.value })} /></label>)}
    <p className="mqtt-control-note">The HUB enforces approved API hosts. WBGT warning must not exceed the high alarm limit. Sensor identities use exact source, site and sensor tuples.</p><button className="mqtt-send" type="submit">Review payload</button>
  </form></Modal>;
}
