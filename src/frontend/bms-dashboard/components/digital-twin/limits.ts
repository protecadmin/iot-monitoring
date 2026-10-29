import type { AlertRuleDto, DeviceDto } from "@/types/api";
import type { Health, Level, Limits, Range, TwinMetric } from "./types";

const ABOVE = new Set(["GreaterThan", "GreaterThanOrEqual"]);
const BELOW = new Set(["LessThan", "LessThanOrEqual"]);

/** Whether an alert rule covers this device: tenant-wide, or scoped to its type, site or the device itself. */
function appliesTo(rule: AlertRuleDto, device: DeviceDto): boolean {
  return (
    (!rule.deviceId || rule.deviceId === device.id) &&
    (!rule.siteId || rule.siteId === device.siteId) &&
    (!rule.deviceTypeId || rule.deviceTypeId === device.deviceTypeId)
  );
}

/** Thresholds of the enabled rules on this metric, for this device, on one side, by severity. */
function thresholds(metric: TwinMetric, rules: AlertRuleDto[], device: DeviceDto, operators: Set<string>, severity: string): number[] {
  return rules
    .filter((r) => r.isEnabled && r.metric === metric.key && r.severity === severity && operators.has(r.operator) && r.threshold != null && appliesTo(r, device))
    .map((r) => r.threshold!);
}

/**
 * A side's two limits. With only a Critical rule, Caution starts where Danger
 * does; with only a Warning rule there is no Danger on that side, so critical
 * is ±Infinity (`beyond`) — a Warning rule must never show as Danger.
 */
function side(warning: number | undefined, critical: number | undefined, beyond: number): Limits | undefined {
  if (warning == null && critical == null) return undefined;
  return { warning: warning ?? critical!, critical: critical ?? beyond };
}

/**
 * Warning and critical limits for a metric, taken from the tenant's enabled
 * "greater than" alert rules that apply to this device (the lowest threshold
 * of each severity — the first to fire). Falls back to the design's defaults
 * for a severity with no rule, so the scale is always drawn.
 */
export function resolveLimits(metric: TwinMetric, rules: AlertRuleDto[], device: DeviceDto): Limits | undefined {
  const first = (severity: string) => Math.min(...thresholds(metric, rules, device, ABOVE, severity), Infinity);
  const finite = (v: number) => (Number.isFinite(v) ? v : undefined);
  return side(finite(first("Warning")) ?? metric.defaultLimits?.warning, finite(first("Critical")) ?? metric.defaultLimits?.critical, Infinity);
}

/**
 * Both sides: the "greater than" limits as above, plus the "less than" ones
 * (the highest threshold of each severity — again the first to fire).
 */
export function resolveRange(metric: TwinMetric, rules: AlertRuleDto[], device: DeviceDto): Range | undefined {
  const high = resolveLimits(metric, rules, device);
  const first = (severity: string) => {
    const v = Math.max(...thresholds(metric, rules, device, BELOW, severity), -Infinity);
    return Number.isFinite(v) ? v : undefined;
  };
  const low = side(first("Warning"), first("Critical"), -Infinity);
  return high || low ? { high, low } : undefined;
}

/** How a value compares with limits on both sides — the worse side wins. */
export function levelOfRange(value: number | undefined, range: Range | undefined): Level {
  if (value == null || !Number.isFinite(value) || !range) return "unknown";
  const high = range.high ? levelOf(value, range.high) : "normal";
  const low = range.low ? (value <= range.low.critical ? "critical" : value <= range.low.warning ? "warning" : "normal") : "normal";
  return RANK[high] >= RANK[low] ? high : low;
}

/** Which side a value is out on, if any. */
export function sideOf(value: number | undefined, range: Range | undefined): "high" | "low" | undefined {
  if (value == null || !range) return undefined;
  if (range.low && value <= range.low.warning) return "low";
  if (range.high && value >= range.high.warning) return "high";
  return undefined;
}

export function levelOf(value: number | undefined, limits: Limits | undefined): Level {
  if (value == null || !Number.isFinite(value) || !limits) return "unknown";
  if (value >= limits.critical) return "critical";
  if (value >= limits.warning) return "warning";
  return "normal";
}

const RANK: Record<Level, number> = { unknown: 0, normal: 1, warning: 2, critical: 3 };

export function worst(levels: Level[]): Level {
  return levels.reduce<Level>((a, b) => (RANK[b] > RANK[a] ? b : a), "unknown");
}

export function healthOf(deviceStatus: string, level: Level): Health {
  return deviceStatus === "Offline" || deviceStatus === "Registered" || deviceStatus === "Decommissioned" ? "offline" : level;
}

/** Display words and colours for each state — one place, so cards, badges and the picture agree. */
export const HEALTH_STYLE: Record<Health, { label: string; text: string; bg: string; ring: string; hex: string }> = {
  normal: { label: "Normal", text: "text-emerald-700 dark:text-emerald-400", bg: "bg-emerald-500/10", ring: "ring-emerald-500/40", hex: "#10b981" },
  warning: { label: "Warning", text: "text-amber-700 dark:text-amber-400", bg: "bg-amber-500/10", ring: "ring-amber-500/50", hex: "#f59e0b" },
  critical: { label: "Critical", text: "text-red-700 dark:text-red-400", bg: "bg-red-500/10", ring: "ring-red-500/50", hex: "#ef4444" },
  unknown: { label: "No data", text: "text-muted-foreground", bg: "bg-muted", ring: "ring-border", hex: "#94a3b8" },
  offline: { label: "Offline", text: "text-muted-foreground", bg: "bg-muted", ring: "ring-border", hex: "#94a3b8" },
};

export function formatReading(value: number | undefined, decimals = 2): string {
  return value == null || !Number.isFinite(value) ? "—" : value.toFixed(decimals);
}
