"use client";

import { useTelemetryHistory } from "@/hooks/useTelemetryHistory";
import { TelemetryRangePicker } from "@/components/charts/TelemetryRangePicker";
import { TelemetryChartView } from "@/components/charts/TelemetryChartView";

export function TelemetryChart({ deviceId }: { deviceId: string }) {
  const { range, setRange, points, series, derivedSeries, loading, error } = useTelemetryHistory(deviceId);

  return (
    <div>
      <TelemetryRangePicker range={range} onChange={setRange} />
      <TelemetryChartView points={points} series={series} derivedSeries={derivedSeries} loading={loading} error={error} />
    </div>
  );
}
