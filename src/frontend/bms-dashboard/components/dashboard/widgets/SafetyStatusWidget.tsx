import { Sparkline } from "@/components/digital-twin/parts/Sparkline";
import { sideOf } from "@/components/digital-twin/limits";
import type { Level } from "@/components/digital-twin/types";
import type { WidgetItem } from "../useWidgetData";
import { DeviceCard, LEVEL_STYLE, LimitBar, Reading } from "./shared";

/** What a status means, in words, for the readings people act on. */
const MEANING: Record<string, Partial<Record<Level, string>>> = {
  vibration: {
    normal: "Normal running (ISO 10816 zone A/B)",
    warning: "Rough running — plan an inspection",
    critical: "Damage risk — inspect now",
  },
  noiseLevel: {
    normal: "Below the 85 dB(A) action level",
    warning: "Hearing protection recommended",
    critical: "Hearing protection required",
  },
  wbgt: {
    normal: "Normal work",
    warning: "Hydrate and take regular breaks",
    critical: "Limit heavy work, rest in shade",
  },
};

const GENERIC: Record<"high" | "low", Partial<Record<Level, string>>> = {
  high: { warning: "Above the caution limit", critical: "Above the danger limit" },
  low: { warning: "Below the caution limit", critical: "Below the danger limit" },
};

/** Good / Caution / Danger for a reading with alert limits — one card per device. */
export function SafetyStatusWidget({ item }: { item: WidgetItem }) {
  const { device, parameter, value, trend, limits, level } = item;
  const out = sideOf(value, limits);
  // The per-reading wording describes the high side; a low reading gets the plain one.
  const meaning =
    (out !== "low" ? MEANING[parameter.key]?.[level] : undefined) ??
    (out ? GENERIC[out][level] : level === "unknown" ? "Waiting for a reading" : "Within limits");
  return (
    <DeviceCard device={device} subtitle={`${parameter.label} · ${item.typeName}`} level={level}>
      <div className="flex items-end gap-3">
        <Reading value={value} unit={parameter.unit ?? ""} />
        <div className="min-w-0 flex-1">
          <Sparkline values={trend} warning={limits?.high?.warning} color={LEVEL_STYLE[level].color} className="h-7" />
        </div>
      </div>
      {meaning && <p className="text-[11px] text-foreground/80 truncate">{meaning}</p>}
      {limits && <LimitBar value={value} range={limits} />}
    </DeviceCard>
  );
}
