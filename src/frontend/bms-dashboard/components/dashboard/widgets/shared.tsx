import Link from "next/link";
import { BellOff } from "lucide-react";
import type { Level, Range } from "@/components/digital-twin/types";
import type { DeviceDto } from "@/types/api";

export const LEVEL_STYLE: Record<Level, { label: string; badge: string; color: string; border: string }> = {
  normal: { label: "Good", badge: "bg-emerald-500/15 text-emerald-700 dark:text-emerald-400", color: "#10b981", border: "border-border" },
  warning: { label: "Caution", badge: "bg-amber-500/15 text-amber-700 dark:text-amber-400", color: "#f59e0b", border: "border-amber-500/50" },
  critical: { label: "Danger", badge: "bg-red-500/15 text-red-700 dark:text-red-400", color: "#ef4444", border: "border-red-500/60" },
  unknown: { label: "No data", badge: "bg-muted text-muted-foreground", color: "#94a3b8", border: "border-border" },
};

/** Worst first, so problems are on top. */
export const LEVEL_ORDER: Record<Level, number> = { critical: 0, warning: 1, normal: 2, unknown: 3 };

export function formatValue(value: number | undefined, decimals = 1) {
  if (value == null || !Number.isFinite(value)) return "—";
  return value.toLocaleString(undefined, { maximumFractionDigits: decimals, minimumFractionDigits: Math.min(decimals, 1) });
}

/** A device that isn't reporting must never look current. */
export function isOffline(device: DeviceDto) {
  return device.status === "Offline";
}

/** How long ago, briefly: "5 min", "3 h", "2 days". */
export function since(iso: string | null) {
  if (!iso) return "never";
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60_000);
  if (minutes < 60) return `${minutes} min`;
  if (minutes < 48 * 60) return `${Math.round(minutes / 60)} h`;
  return `${Math.round(minutes / 1440)} days`;
}

/**
 * Card frame shared by the per-device widgets: the device, its status and a
 * link to its page. `level` null means the reading has no limits to judge it
 * by, so no status badge is shown.
 */
export function DeviceCard({
  device,
  subtitle,
  level,
  children,
}: {
  device: DeviceDto;
  subtitle: string;
  level: Level | null;
  children: React.ReactNode;
}) {
  const offline = isOffline(device);
  const style = offline ? LEVEL_STYLE.unknown : level ? LEVEL_STYLE[level] : null;
  const muted = device.alertsEnabled === false;
  return (
    <Link
      href={`/devices/${device.id}`}
      className={`block rounded-lg border bg-card px-3 py-2.5 shadow-sm hover:border-primary transition-colors ${offline ? "border-border opacity-75" : (style?.border ?? "border-border")}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[13px] font-medium leading-tight text-foreground truncate">{device.name}</div>
          <div className="text-[11px] text-muted-foreground truncate">{subtitle}</div>
        </div>
        <div className="flex shrink-0 items-center gap-1.5">
          {muted && (
            <span title="Alerts muted (maintenance)">
              <BellOff className="h-3.5 w-3.5 text-amber-600 dark:text-amber-400" aria-label="Alerts muted" />
            </span>
          )}
          {style && <span className={`px-1.5 py-0.5 rounded text-[11px] font-medium leading-none ${style.badge}`}>{offline ? "Offline" : style.label}</span>}
        </div>
      </div>
      {offline ? (
        <p className="mt-2 text-xs text-muted-foreground">No reading for {since(device.lastTelemetryAt)}.</p>
      ) : (
        <div className="mt-1.5 space-y-1.5">{children}</div>
      )}
    </Link>
  );
}

/** The headline number and its unit. */
export function Reading({ value, unit, decimals = 1 }: { value: number | undefined; unit: string; decimals?: number }) {
  return (
    <div className="flex shrink-0 items-baseline gap-1">
      <span className="text-xl font-semibold leading-none text-foreground tabular-nums">{formatValue(value, decimals)}</span>
      <span className="text-xs text-muted-foreground">{unit}</span>
    </div>
  );
}

/**
 * A thin Good / Caution / Danger bar with a marker at the current value and
 * the limits under it — the same words as the status badge. Two-sided when
 * the reading has "less than" rules too: red | amber | green | amber | red.
 */
export function LimitBar({ value, range }: { value: number | undefined; range: Range }) {
  const { high, low } = range;
  // Scaled around the limits, not from zero, so close limits (30 / 32 °C)
  // still get a readable band.
  // Finite limits only: a side with just a Warning rule has no Danger point.
  const fin = (v: number | undefined): v is number => v != null && Number.isFinite(v);
  const points = [low?.critical, low?.warning, high?.warning, high?.critical].filter(fin);
  const minP = Math.min(...points);
  const maxP = Math.max(...points);
  let from: number;
  let to: number;
  if (low && high) {
    const span = maxP - minP || Math.abs(maxP) * 0.1 || 1;
    from = minP - span * 0.4;
    to = maxP + span * 0.4;
  } else if (high) {
    const gap = (fin(high.critical) ? high.critical - high.warning : 0) || Math.abs(high.warning) * 0.1 || 1;
    from = high.warning - 2 * gap;
    if (from < 0 && high.warning >= 0) from = 0;
    to = maxP + 1.5 * gap;
  } else {
    const gap = (fin(low!.critical) ? low!.warning - low!.critical : 0) || Math.abs(low!.warning) * 0.1 || 1;
    from = minP - 1.5 * gap;
    to = low!.warning + 2 * gap;
  }
  const pct = (v: number) => Math.max(0, Math.min(100, ((v - from) / (to - from)) * 100));

  // Bands left to right; a side with one rule has no amber band.
  const bands: { end: number; className: string }[] = [];
  if (low) bands.push({ end: pct(low.critical), className: "bg-red-500" }, { end: pct(low.warning), className: "bg-amber-500" });
  if (high) bands.push({ end: pct(high.warning), className: "bg-emerald-500" }, { end: pct(high.critical), className: "bg-amber-500" }, { end: 100, className: "bg-red-500" });
  else bands.push({ end: 100, className: "bg-emerald-500" });

  const labels = [...new Set(points)];
  const label = (v: number) => v.toLocaleString(undefined, { maximumFractionDigits: 2 });
  let start = 0;
  return (
    <div>
      <div className="relative h-1.5 rounded-full">
        <div className="absolute inset-0 flex overflow-hidden rounded-full">
          {bands.map((b, i) => {
            const width = Math.max(0, b.end - start);
            start = Math.max(start, b.end);
            return <div key={i} className={b.className} style={{ width: `${width}%` }} />;
          })}
        </div>
        {value != null && Number.isFinite(value) && (
          <div className="absolute -top-1 h-3.5 w-1 -translate-x-1/2 rounded-sm bg-foreground ring-2 ring-card" style={{ left: `${pct(value)}%` }} />
        )}
      </div>
      <div className="relative mt-0.5 h-3 text-[10px] leading-3 text-muted-foreground tabular-nums">
        {labels.map((v) => (
          <span key={v} className="absolute -translate-x-1/2" style={{ left: `${pct(v)}%` }}>
            {label(v)}
          </span>
        ))}
      </div>
    </div>
  );
}
