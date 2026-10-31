import { measurementDecimals } from "@/lib/format";
import { Sparkline } from "@/components/digital-twin/parts/Sparkline";
import type { Level } from "@/components/digital-twin/types";
import type { WidgetItem } from "../useWidgetData";
import { LEVEL_STYLE, formatValue } from "./shared";

/**
 * One reading as a row of a device's card. A row's status is null when the
 * reading has nothing to judge it by (no alert limits) — it never counts as
 * "Good" or bad then.
 */
export type RowKind = "LiveValue" | "LevelGauge" | "OnOff";

/** Status of a row, used for the card's badge and to put the worst row first. */
export function rowLevel(item: WidgetItem): Level | null {
  if (item.value == null) return item.kind === "OnOff" || item.limits || item.kind === "LevelGauge" ? "unknown" : null;
  if (item.kind === "OnOff") return null;
  if (item.limits) return item.level;
  if (item.kind === "LevelGauge") {
    // With no rule for it, a level is flagged when low — a tank running dry.
    const pct = Math.max(0, Math.min(100, item.value));
    return pct <= 10 ? "critical" : pct <= 20 ? "warning" : "normal";
  }
  return null;
}

function RowFrame({ item, level, children }: { item: WidgetItem; level: Level | null; children: React.ReactNode }) {
  const flagged = level === "warning" || level === "critical";
  return (
    <div className="flex items-center gap-2 min-w-0">
      <span className="w-24 shrink-0 truncate text-[11px] text-muted-foreground" title={item.parameter.label}>
        {flagged && <span className="mr-1 inline-block h-1.5 w-1.5 rounded-full align-middle" style={{ backgroundColor: LEVEL_STYLE[level].color }} />}
        {item.parameter.label}
      </span>
      {children}
    </div>
  );
}

function Value({ value, unit, decimals }: { value: number | undefined; unit: string; decimals: number }) {
  return (
    <span className={`${decimals === 4 ? "w-28" : "w-20"} shrink-0 text-sm font-semibold text-foreground tabular-nums whitespace-nowrap`}>
      {formatValue(value, decimals)} <span className="text-[11px] font-normal text-muted-foreground">{unit}</span>
    </span>
  );
}

/** A live reading — kW, m³/h, °C — with its last hour. */
export function LiveValueRow({ item }: { item: WidgetItem }) {
  const level = rowLevel(item);
  const color = level && level !== "unknown" ? LEVEL_STYLE[level].color : "#3b82f6";
  const decimals = measurementDecimals(item.parameter.key);
  const peak = item.trend.length ? Math.max(...item.trend) : undefined;
  return (
    <RowFrame item={item} level={level}>
      <Value value={item.value} unit={item.parameter.unit ?? ""} decimals={decimals} />
      <div className="min-w-0 flex-1" title={peak != null ? `Peak last hour: ${formatValue(peak, decimals)} ${item.parameter.unit ?? ""}` : undefined}>
        <Sparkline values={item.trend} warning={item.limits?.high?.warning} color={color} className="h-5" />
      </div>
    </RowFrame>
  );
}

/** A 0–100 % level — a tank, a load — as a fill bar. */
export function LevelGaugeRow({ item }: { item: WidgetItem }) {
  const level = rowLevel(item);
  const pct = item.value == null ? 0 : Math.max(0, Math.min(100, item.value));
  return (
    <RowFrame item={item} level={level}>
      <Value value={item.value} unit={item.parameter.unit ?? "%"} decimals={0} />
      <div className="h-2 flex-1 rounded-full bg-muted overflow-hidden" aria-label={`${formatValue(pct, 0)} %`}>
        <div className="h-full rounded-full transition-all" style={{ width: `${pct}%`, backgroundColor: LEVEL_STYLE[level ?? "normal"].color }} />
      </div>
    </RowFrame>
  );
}

/** An on/off state — pump running, door open. Any non-zero value is on. */
export function OnOffRow({ item }: { item: WidgetItem }) {
  const on = item.value != null && item.value !== 0;
  return (
    <RowFrame item={item} level={null}>
      <span className="flex items-center gap-1.5 text-sm font-semibold text-foreground">
        <span className={`h-2 w-2 rounded-full ${item.value == null ? "bg-muted-foreground" : on ? "bg-emerald-500" : "bg-slate-400"}`} />
        {item.value == null ? "—" : on ? "On" : "Off"}
      </span>
    </RowFrame>
  );
}
