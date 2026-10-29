"use client";

import { useMemo, useState } from "react";
import { Settings2 } from "lucide-react";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { useTelemetryHistory } from "@/hooks/useTelemetryHistory";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { AlertsApi, SitesApi } from "@/lib/api/resources";
import type { DeviceDto, TelemetryPointDto } from "@/types/api";
import type { ResolvedTwin, TwinMetric } from "./types";
import { TwinSettingsModal } from "./TwinSettingsModal";
import { HEALTH_STYLE, formatReading, healthOf, levelOf, resolveLimits } from "./limits";
import { SeverityScale } from "./parts/SeverityScale";
import { Sparkline } from "./parts/Sparkline";

function latest(points: TelemetryPointDto[], key: string): number | undefined {
  for (let i = points.length - 1; i >= 0; i--) {
    const v = points[i].fields?.[key];
    if (v != null) return v;
  }
  return undefined;
}

/**
 * The live digital twin of one device: its equipment drawn from the chosen template,
 * the sensor marked on it, and readings that update with every message. The
 * picture, cards and scale all take one health colour, worked out from the
 * same alert-rule limits that raise alerts.
 */
export function DigitalTwinView({
  device,
  twin,
  deviceTypeName,
  onDeviceChange,
}: {
  device: DeviceDto;
  twin: ResolvedTwin;
  deviceTypeName?: string;
  /** Called with the saved device after the twin settings change, so the parent can re-resolve the twin. */
  onDeviceChange?: (device: DeviceDto) => void;
}) {
  const { profile, template, mountPoint } = twin;
  const canCustomize = useCan()("devices.edit");
  const [customizing, setCustomizing] = useState(false);
  const { points } = useTelemetryHistory(device.id);
  // Each entry is one device's effective rule, so only this device's apply (see limits.ts).
  const rulesQuery = useApi(() => AlertsApi.effectiveRules(device.tenantId), [device.tenantId]);
  const siteQuery = useApi(() => SitesApi.get(device.siteId, device.tenantId), [device.siteId, device.tenantId]);
  const [status, setStatus] = useState(device.status);
  const [lastSeen, setLastSeen] = useState(device.lastSeenAt);

  useRealtime({
    DeviceStatusChanged: (payload) => {
      const p = payload as { id: string; status: string; lastSeenAt: string | null };
      if (p.id !== device.id) return;
      setStatus(p.status);
      setLastSeen(p.lastSeenAt);
    },
  });

  const rules = useMemo(() => rulesQuery.data ?? [], [rulesQuery.data]);
  const limits = useMemo(() => resolveLimits(profile.primary, rules, device), [profile.primary, rules, device]);
  const primaryValue = latest(points, profile.primary.key);
  const health = healthOf(status, levelOf(primaryValue, limits));
  const style = HEALTH_STYLE[health];
  const running = health !== "offline" && health !== "unknown";
  const lastReading = points.length ? new Date(points[points.length - 1].timestamp) : null;
  const trend = points.map((p) => p.fields?.[profile.primary.key]).filter((v): v is number => v != null).slice(-60);
  const secondary = profile.secondary.filter((m) => latest(points, m.key) != null);
  const { Illustration } = template;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-xl font-semibold text-foreground truncate">{device.name} · Digital Twin</h2>
          <p className="text-sm text-muted-foreground">
            {twin.assetName} · {siteQuery.data?.name ?? "…"} · live condition monitoring
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
        {canCustomize && (
          <button
            onClick={() => setCustomizing(true)}
            className="flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-md border border-input hover:bg-muted"
            title="Choose the equipment picture and where the sensor sits"
          >
            <Settings2 className="h-3.5 w-3.5" /> Customize twin
          </button>
        )}
        <div className={`flex items-center gap-2 rounded-full px-4 py-1.5 text-sm ring-1 ${style.bg} ${style.ring}`}>
          <span className="relative flex h-2.5 w-2.5">
            {running && <span className="motion-safe:animate-ping absolute inline-flex h-full w-full rounded-full opacity-60" style={{ background: style.hex }} />}
            <span className="relative inline-flex h-2.5 w-2.5 rounded-full" style={{ background: style.hex }} />
          </span>
          <span className={`font-semibold uppercase tracking-wide ${style.text}`}>{health === "offline" ? "Offline" : health === "normal" ? "Online" : style.label}</span>
          <span className="text-muted-foreground">· last reading {lastReading ? lastReading.toLocaleTimeString() : "—"}</span>
        </div>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_300px] xl:grid-cols-[260px_minmax(0,1fr)_300px] items-start">
        {/* Asset + scale */}
        <div className="space-y-4 order-3 xl:order-1">
          <div className="bg-card border border-border rounded-xl p-4 shadow-sm">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">Asset</div>
            <div className="text-base font-semibold text-foreground mt-1 mb-3">{twin.assetName}</div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
              <dt className="text-muted-foreground">Device</dt>
              <dd className="text-foreground font-mono text-xs self-center truncate">{device.deviceCode}</dd>
              <dt className="text-muted-foreground">Sensor</dt>
              <dd className="text-foreground truncate">{deviceTypeName ?? "—"}</dd>
              <dt className="text-muted-foreground">Sensor at</dt>
              <dd className="text-foreground truncate">{mountPoint.label}</dd>
              <dt className="text-muted-foreground">Site</dt>
              <dd className="text-foreground truncate">{siteQuery.data?.name ?? "—"}</dd>
              <dt className="text-muted-foreground">Last seen</dt>
              <dd className="text-foreground">{lastSeen ? new Date(lastSeen).toLocaleString() : "Never"}</dd>
              <dt className="text-muted-foreground">Health</dt>
              <dd className={`font-semibold ${style.text}`}>{health === "normal" ? "Good" : style.label}</dd>
            </dl>
          </div>
          {limits && (
            <div className="bg-card border border-border rounded-xl p-4 shadow-sm space-y-2">
              <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">{profile.primary.label} severity</div>
              <SeverityScale value={primaryValue} limits={limits} unit={profile.primary.unit} decimals={profile.primary.decimals} />
            </div>
          )}
        </div>

        {/* Equipment picture */}
        <div className="order-1 xl:order-2 min-w-0 rounded-xl border border-border bg-gradient-to-b from-muted/40 to-card p-4 shadow-sm">
          <div className="relative mx-auto max-w-[620px]">
            <Illustration health={health} running={running} />
            <div
              className="absolute -translate-x-1/2 -translate-y-1/2"
              style={{ left: `${(mountPoint.x / template.viewBox.width) * 100}%`, top: `${(mountPoint.y / template.viewBox.height) * 100}%` }}
              title={`Sensor · ${mountPoint.label}`}
            >
              <span className="relative flex h-7 w-7 items-center justify-center">
                {running && <span className="motion-safe:animate-ping absolute inline-flex h-full w-full rounded-full opacity-40" style={{ background: style.hex }} />}
                <span
                  className="relative inline-flex h-6 w-6 items-center justify-center rounded-full text-[11px] font-bold text-white ring-2 ring-white"
                  style={{ background: style.hex }}
                >
                  S
                </span>
              </span>
            </div>
          </div>
          <p className="text-center text-xs text-muted-foreground mt-2">
            <span className="font-semibold text-foreground">{twin.assetName}</span> · sensor at {mountPoint.label.toLowerCase()}
          </p>
        </div>

        {/* Readings */}
        <div className="space-y-3 order-2 xl:order-3">
          <div className={`bg-card rounded-xl p-4 shadow-sm ring-2 ${style.ring} border border-transparent`}>
            <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">{profile.primary.label}</div>
            <div className="mt-1 flex items-baseline gap-1.5">
              <span className="text-4xl font-bold text-foreground tabular-nums">{formatReading(primaryValue, profile.primary.decimals)}</span>
              <span className="text-sm text-muted-foreground">{profile.primary.unit}</span>
            </div>
            <div className={`text-sm font-semibold mt-1 ${style.text}`}>
              ● {style.label}
              {limits && health !== "offline" && <span className="font-normal text-muted-foreground"> · warning at {limits.warning}</span>}
            </div>
            <div className="mt-3">
              <Sparkline values={trend} warning={limits?.warning} color={style.hex} />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            {secondary.map((m) => (
              <ReadingCard key={m.key} metric={m} value={latest(points, m.key)} />
            ))}
          </div>
          {secondary.length === 0 && points.length === 0 && (
            <p className="text-xs text-muted-foreground">Waiting for the first reading from this device.</p>
          )}
        </div>
      </div>

      {customizing && (
        <TwinSettingsModal device={device} twin={twin} onClose={() => setCustomizing(false)} onSaved={(d) => onDeviceChange?.(d)} />
      )}
    </div>
  );
}

function ReadingCard({ metric, value }: { metric: TwinMetric; value: number | undefined }) {
  return (
    <div className="bg-card border border-border rounded-xl p-3 shadow-sm min-w-0">
      <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground truncate">{metric.label}</div>
      <div className="mt-0.5 flex items-baseline gap-1">
        <span className="text-2xl font-bold text-foreground tabular-nums">{formatReading(value, metric.decimals)}</span>
        <span className="text-xs text-muted-foreground">{metric.unit}</span>
      </div>
    </div>
  );
}
