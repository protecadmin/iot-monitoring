"use client";

import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { LoadingState, ErrorState } from "@/components/ui/States";
import { formatMeasurement } from "@/lib/format";
import { derivedOrMeasured, periodKeysOf } from "@/hooks/useTelemetryHistory";
import type { DeviceTypeParameterDto, TelemetryPointDto } from "@/types/api";

const COLORS = ["var(--chart-1, #2563eb)", "var(--chart-2, #059669)", "var(--chart-3, #d97706)", "var(--chart-4, #7c3aed)", "var(--chart-5, #dc2626)", "var(--chart-6, #0891b2)", "var(--chart-7, #c026d3)"];
const DERIVED_PREFIX = "ƒ ";

type Row = Record<string, number | string> & { time: string; index: number };

/** A series' unit, as its device-type parameter declares it. */
function unitOf(key: string, parameters: DeviceTypeParameterDto[]): string {
  return (parameters.find((p) => p.key.toLowerCase() === key.toLowerCase())?.unit ?? "").trim().toLowerCase();
}

/**
 * The period covering `index` for a period-stamped key: the next such value
 * forward in time, since a windowed value is stamped at the end of the span it
 * describes. Returns the span too, so a tooltip can name the period rather
 * than imply the value belongs to the instant being hovered.
 */
function coveringPeriod(rows: Row[], index: number, dataKey: string): { value: number; from?: string; to: string } | undefined {
  for (let i = index; i < rows.length; i++) {
    if (typeof rows[i][dataKey] !== "number") continue;
    for (let j = i - 1; j >= 0; j--) {
      if (typeof rows[j][dataKey] === "number") return { value: rows[i][dataKey] as number, from: rows[j].time, to: rows[i].time };
    }
    return { value: rows[i][dataKey] as number, to: rows[i].time };
  }
  return undefined;
}

export function TelemetryChartView({
  points,
  series,
  derivedSeries = [],
  parameters = [],
  loading,
  error,
}: {
  points: TelemetryPointDto[];
  series: string[];
  derivedSeries?: string[];
  /** The device type's parameters, for units — without them every calculated series keeps its own axis. */
  parameters?: DeviceTypeParameterDto[];
  loading: boolean;
  error: string | null;
}) {
  // Keep the same height while fetching a new range so the page does not
  // shrink and move the user's scroll position on every range change.
  if (loading) return <div className="flex h-[260px] items-center justify-center" aria-busy="true"><LoadingState label="Loading telemetry..." /></div>;
  if (error) return <div className="flex h-[260px] items-center justify-center"><ErrorState message={error} /></div>;

  // Derived keys are prefixed so a calculated series can never collide with,
  // or be mistaken for, a measured field of the same name.
  const chartData: Row[] = points.map((p, index) => {
    const row = { ...(p.fields ?? {}), time: new Date(p.timestamp).toLocaleTimeString(), index } as Row;
    // One line per calculated key, using the measured value where the device
    // sent it — the same value the readings table shows.
    for (const key of derivedSeries) {
      const value = derivedOrMeasured(p, key);
      if (value) row[DERIVED_PREFIX + key] = value.value;
    }
    return row;
  });

  if (chartData.length === 0) return <p className="flex h-[260px] items-center justify-center text-sm text-muted-foreground text-center">No telemetry yet for this device.</p>;

  const periodKeys = new Set(periodKeysOf(points, derivedSeries));
  const measuredUnits = new Set(series.map((key) => unitOf(key, parameters)).filter(Boolean));

  /**
   * A calculated series shares the readings' axis when it is in the same unit
   * as one of them — an Leq or a time-weighted average is the same quantity as
   * the readings it averages, and drawing it against its own scale puts it at
   * a different height for the same number.
   *
   * Consumption stays on its own axis: it is typically orders of magnitude
   * smaller than the readings it comes from (0.01 kWh beside 230 V) and would
   * flatten into the baseline on a shared one.
   */
  const sharesMeasuredAxis = (key: string) => {
    const unit = unitOf(key, parameters);
    return unit !== "" && measuredUnits.has(unit);
  };
  const needsOwnAxis = derivedSeries.some((key) => !sharesMeasuredAxis(key));

  return (
    <ResponsiveContainer width="100%" height={260}>
      <LineChart data={chartData}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey="time" tick={{ fontSize: 11 }} minTickGap={30} />
        <YAxis yAxisId="measured" tick={{ fontSize: 11 }} />
        {needsOwnAxis && <YAxis yAxisId="derived" orientation="right" tick={{ fontSize: 11 }} tickFormatter={(v: number) => formatMeasurement(v)} />}
        <Tooltip
          content={({ active, payload }) => {
            if (!active || !payload?.length) return null;
            const row = payload[0]?.payload as Row | undefined;
            if (!row) return null;
            const shown = new Set(payload.map((entry) => String(entry.dataKey)));

            return (
              <div className="rounded-md border border-border bg-card px-3 py-2 shadow-sm text-xs">
                <div className="font-medium text-foreground mb-1">{row.time}</div>
                {payload.map((entry) => (
                  <div key={String(entry.dataKey)} style={{ color: entry.color }} className="tabular-nums">
                    {String(entry.dataKey)} : {typeof entry.value === "number" ? formatMeasurement(entry.value, series.includes(String(entry.dataKey)) ? String(entry.dataKey) : undefined) : entry.value}
                  </div>
                ))}
                {/* A windowed value exists only at its period's end, so hovering
                    a reading inside that period would otherwise say nothing
                    about it. Naming the span keeps the value attached to the
                    period it describes rather than to this instant. */}
                {derivedSeries
                  .filter((key) => periodKeys.has(key) && !shown.has(DERIVED_PREFIX + key))
                  .map((key) => {
                    const period = coveringPeriod(chartData, row.index, DERIVED_PREFIX + key);
                    if (!period) return null;
                    return (
                      <div key={key} className="tabular-nums text-muted-foreground mt-1">
                        {DERIVED_PREFIX}
                        {key} ({period.from ? `${period.from} – ${period.to}` : `to ${period.to}`}) : {formatMeasurement(period.value)}
                      </div>
                    );
                  })}
              </div>
            );
          }}
        />
        <Legend wrapperStyle={{ fontSize: 12 }} />
        {series.map((key, i) => (
          <Line key={key} yAxisId="measured" type="monotone" dataKey={key} stroke={COLORS[i % COLORS.length]} dot={false} connectNulls isAnimationActive={false} />
        ))}
        {derivedSeries.map((key, i) => (
          <Line
            key={`derived-${key}`}
            yAxisId={sharesMeasuredAxis(key) ? "measured" : "derived"}
            // A windowed value is stamped at the end of the span it covers, so
            // stepBefore holds it back across that span; stepAfter would draw
            // it over the following period instead — one period late. A
            // consumption delta is stamped the same way and arguably wants
            // this too, but it is left as it was rather than changed here.
            type={periodKeys.has(key) ? "stepBefore" : "stepAfter"}
            dataKey={DERIVED_PREFIX + key}
            stroke={COLORS[(series.length + i) % COLORS.length]}
            strokeDasharray="5 3"
            dot={false}
            connectNulls
            isAnimationActive={false}
          />
        ))}
      </LineChart>
    </ResponsiveContainer>
  );
}
