export type SensorTuple = { source: string; site: string; sensor: string };
export type HubSnapshot = {
  v: number; hub: string; firmware?: string; config_version: number; time_utc?: string;
  site?: { name?: string; location?: string; timezone?: string };
  network?: { type?: string; interface?: string; signal_percent?: number | null; internet?: boolean };
  tv?: { mode?: string; welcome_seconds?: number; rotation_seconds?: number; selected?: number; live?: number; showing?: number; selected_sensors?: SensorTuple[]; cards?: Array<{
    device_id?: string; sensor: string; label: string; state?: string; measured_at?: number; alert_state?: string;
    channels?: Array<{ key: string; label?: string; value: number | null; unit?: string; valid?: boolean; limit?: number | null; limit_status?: string }>;
  }> };
  devices?: Array<SensorTuple & { state?: string; age_seconds?: number; latest_value?: number | null; selected?: boolean }>;
  api?: { enabled?: boolean; connected?: boolean; url?: string; interval_s?: number };
  thresholds?: { api_alert_limits?: Record<string, number> };
  branding?: { has_client_logo?: boolean; client_logo_revision?: string };
  system?: { runtime_healthy?: boolean; health?: Record<string, unknown> };
  capabilities?: Record<string, boolean>;
  ota?: Record<string, unknown>;
};
export type HubCommand = {
  commandId: string; action: string; state: string; issuedAt: string; deadline: string;
  code?: string; message?: string; acknowledgedAt?: string; deviceTimeUtc?: string; appliedVersion?: number; actor?: string;
  data: Record<string, unknown>; ifVersion?: number; payload?: string;
};
export type CapturedMessage = { topic: string; receivedAt: string; payload: string; bytes: number };
export type HubMonitor = { messages: CapturedMessage[]; commandsToday: number; successRate: number | null; broker: { connected: boolean; tls: boolean; host: string; port: number } };
export function parseSnapshot(json?: string | null): HubSnapshot | null {
  try { const s = JSON.parse(json || "null"); return s?.v === 2 && typeof s.config_version === "number" ? s : null; }
  catch { return null; }
}
export const tupleKey = (s: SensorTuple) => JSON.stringify([s.source, s.site, s.sensor]);
