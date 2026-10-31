"use client";

import { derivedOrMeasured, useTelemetryHistory } from "@/hooks/useTelemetryHistory";
import { StatCard } from "@/components/ui/States";
import { TelemetryRangePicker } from "@/components/charts/TelemetryRangePicker";
import { TelemetryChartView } from "@/components/charts/TelemetryChartView";
import { TelemetryTable } from "@/components/charts/TelemetryTable";
import { formatMeasurement, humanizeKey } from "@/lib/format";
import type { DeviceTypeParameterDto, TelemetryPointDto } from "@/types/api";

/** Newest value for a key. A reading and the value calculated from it can arrive as separate pushes, so the newest point doesn't always carry every key. */
function latestValue(points: TelemetryPointDto[], key: string, source: "fields" | "derived"): number | undefined {
  for (let i = points.length - 1; i >= 0; i--) {
    // A calculated key shows the device's own value where it measured one.
    const value = source === "derived" ? derivedOrMeasured(points[i], key)?.value : points[i].fields?.[key];
    if (value !== undefined) return value;
  }
  return undefined;
}

/** Current value tiles, chart, and readings table — one shared fetch/subscription (see useTelemetryHistory) drives all three, kept in sync off a single range picker. */
export function LiveTab({
  deviceId,
  parameters = [],
  timeSource = "Device",
}: {
  deviceId: string;
  parameters?: DeviceTypeParameterDto[];
  timeSource?: "Device" | "Received";
}) {
  const { range, setRange, points, series, derivedSeries, loading, error } = useTelemetryHistory(deviceId);
  const latest = points[points.length - 1];

  return (
    <div className="space-y-4">
      {latest ? (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {series.map((key) => {
            const value = latestValue(points, key, "fields");
            return <StatCard key={key} label={parameters.find((p) => p.key === key)?.label ?? humanizeKey(key)} value={value !== undefined ? formatMeasurement(value, key) : "—"} />;
          })}
          {derivedSeries.map((key) => {
            const value = latestValue(points, key, "derived");
            return (
              <StatCard key={`derived-${key}`} label={`ƒ ${humanizeKey(key)} (calculated)`} value={value !== undefined ? formatMeasurement(value) : "—"} />
            );
          })}
        </div>
      ) : (
        !loading && <p className="text-sm text-muted-foreground">No telemetry recorded yet.</p>
      )}
      <p className="text-xs text-muted-foreground">
        {latest ? `Last update: ${new Date(latest.timestamp).toLocaleTimeString()}` : "Waiting for telemetry..."}
        {derivedSeries.length > 0 && " · ƒ marks values calculated from measured telemetry (see Calculations)."}
      </p>

      <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
        <TelemetryRangePicker range={range} onChange={setRange} />
        <TelemetryChartView points={points} series={series} derivedSeries={derivedSeries} parameters={parameters} loading={loading} error={error} />
      </div>

      <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
        <h3 className="text-sm font-semibold text-foreground mb-3">Readings</h3>
        <TelemetryTable points={points} series={series} derivedSeries={derivedSeries} loading={loading} error={error} timeLabel={timeSource === "Received" ? "Received at" : "Time"} />
      </div>
    </div>
  );
}
