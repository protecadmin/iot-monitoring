import type { Limits } from "../types";
import { formatReading } from "../limits";

// Fixed zone widths, so the warning band stays readable however narrow it is
// in real units (noise: 85–90 dB is 5 dB; vibration: 4.5–7.1 mm/s). The
// marker is placed proportionally within whichever zone the value falls in.
const NORMAL_END = 50;
const WARNING_END = 75;

function markerPercent(value: number, limits: Limits): number {
  const { warning, critical } = limits;
  if (value <= warning) return (Math.max(0, value) / (warning || 1)) * NORMAL_END;
  if (value <= critical) return NORMAL_END + ((value - warning) / (critical - warning || 1)) * (WARNING_END - NORMAL_END);
  // Above critical: fill the red zone over a span as wide as the warning band (at least 15 % of the limit).
  const span = Math.max(critical - warning, critical * 0.15) || 1;
  return Math.min(100, WARNING_END + ((value - critical) / span) * (100 - WARNING_END));
}

/** Green / amber / red bar for the headline reading, with a marker at the current value. */
export function SeverityScale({ value, limits, unit, decimals }: { value: number | undefined; limits: Limits; unit: string; decimals?: number }) {
  const hasValue = value != null && Number.isFinite(value);
  const left = hasValue ? markerPercent(value, limits) : 0;

  return (
    <div className="space-y-1.5">
      <div className="relative h-5">
        {hasValue && (
          <div
            className="absolute flex flex-col items-center"
            style={{ left: `${left}%`, transform: `translateX(${left < 12 ? "-15%" : left > 88 ? "-85%" : "-50%"})` }}
          >
            <span className="text-[11px] font-semibold text-foreground tabular-nums whitespace-nowrap">now {formatReading(value, decimals)}</span>
            <span className="w-0 h-0 border-x-[6px] border-x-transparent border-t-[7px] border-t-foreground" />
          </div>
        )}
      </div>
      <div className="grid grid-cols-[2fr_1fr_1fr] h-10 rounded-md overflow-hidden text-[11px] font-semibold leading-tight text-center">
        <div className="bg-emerald-500 text-white flex flex-col justify-center px-1">
          Normal
          <span className="font-normal opacity-90 tabular-nums">&lt; {limits.warning}</span>
        </div>
        <div className="bg-amber-400 text-slate-900 flex flex-col justify-center px-1">
          Warning
          <span className="font-normal opacity-90 tabular-nums whitespace-nowrap">
            {limits.warning}–{limits.critical}
          </span>
        </div>
        <div className="bg-red-500 text-white flex flex-col justify-center px-1">
          Critical
          <span className="font-normal opacity-90 tabular-nums">&gt; {limits.critical}</span>
        </div>
      </div>
      <p className="text-[11px] text-muted-foreground">
        In {unit}. Limits come from this site&apos;s alert rules, so an alert is raised when the marker crosses into amber or red.
      </p>
    </div>
  );
}
