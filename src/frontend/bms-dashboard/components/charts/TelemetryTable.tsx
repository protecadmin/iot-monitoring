"use client";

import { Fragment, useMemo, useState } from "react";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { formatMeasurement } from "@/lib/format";
import { derivedOrMeasured, isPeriodResult, periodKeysOf } from "@/hooks/useTelemetryHistory";
import type { TelemetryPointDto } from "@/types/api";

/** Caps rendered rows — a 7-day range can carry thousands of raw points, and the chart already shows the trend; this is for scanning/verifying exact recent values, not a full data browser. */
const MAX_ROWS = 500;

/** The readings of one period, and the value they produced — absent while the period is still being filled. */
type Period = { result: TelemetryPointDto | null; readings: TelemetryPointDto[] };

/**
 * Splits newest-first points into periods. A period result closes the readings
 * that follow it in this order (they are older, so they are the ones it
 * covers); readings ahead of the newest result belong to the period still
 * open, which has no value yet.
 */
function toPeriods(newestFirst: TelemetryPointDto[]): Period[] {
  const periods: Period[] = [];
  let current: Period = { result: null, readings: [] };

  for (const point of newestFirst) {
    if (isPeriodResult(point)) {
      if (current.result !== null || current.readings.length > 0) periods.push(current);
      current = { result: point, readings: [] };
    } else {
      current.readings.push(point);
    }
  }
  if (current.result !== null || current.readings.length > 0) periods.push(current);
  return periods;
}

export function TelemetryTable({
  points,
  series,
  derivedSeries = [],
  loading,
  error,
  timeLabel = "Time",
}: {
  points: TelemetryPointDto[];
  series: string[];
  derivedSeries?: string[];
  loading: boolean;
  error: string | null;
  /** "Received at" for devices that send no time of their own. */
  timeLabel?: string;
}) {
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});

  // Newest first — matches how every other list in the app (commands, alerts) orders by recency.
  const rows = useMemo(() => [...points].reverse().slice(0, MAX_ROWS), [points]);

  // A calculated key that only ever appears without measured values is a
  // period's result and belongs in a period's heading; one that rides along
  // with readings stays a column beside them.
  const { periodKeys, inlineKeys } = useMemo(() => {
    const periodic = new Set(periodKeysOf(rows, derivedSeries));
    return { periodKeys: derivedSeries.filter((k) => periodic.has(k)), inlineKeys: derivedSeries.filter((k) => !periodic.has(k)) };
  }, [rows, derivedSeries]);

  const periods = useMemo(() => (periodKeys.length > 0 ? toPeriods(rows) : []), [rows, periodKeys.length]);

  if (loading) return <LoadingState label="Loading telemetry..." />;
  if (error) return <ErrorState message={error} />;
  if (points.length === 0) return <EmptyState message="No telemetry yet for this device." />;

  const columnCount = 1 + series.length + inlineKeys.length;

  function ReadingRow({ point }: { point: TelemetryPointDto }) {
    return (
      <tr>
        <td className="px-4 py-2 text-muted-foreground text-xs whitespace-nowrap">{new Date(point.timestamp).toLocaleString()}</td>
        {series.map((key) => (
          <td key={key} className="px-4 py-2 text-foreground tabular-nums">
            {point.fields?.[key] !== undefined ? formatMeasurement(point.fields[key], key) : "—"}
          </td>
        ))}
        {inlineKeys.map((key) => {
          const cell = derivedOrMeasured(point, key);
          return (
            <td
              key={`derived-${key}`}
              className={`px-4 py-2 tabular-nums bg-primary/5 ${cell?.measured ? "text-foreground" : "text-primary"}`}
              title={cell?.measured ? "Measured by the device — not calculated for this reading" : undefined}
            >
              {cell ? formatMeasurement(cell.value) : "—"}
            </td>
          );
        })}
      </tr>
    );
  }

  return (
    <div>
      {points.length > MAX_ROWS && (
        <p className="text-xs text-muted-foreground mb-2">
          Showing the most recent {MAX_ROWS} of {points.length} points. Use Reports → CSV export for the full range.
        </p>
      )}
      {periodKeys.length > 0 && (
        <p className="text-xs text-muted-foreground mb-2">
          Grouped by averaging period — each heading is one completed window and the readings it was calculated from. Click a heading to collapse it.
        </p>
      )}
      <div className="max-h-[420px] overflow-y-auto border border-border rounded-lg">
        <table className="w-full text-sm">
          {/* Solid bg-muted, not bg-muted/50 — this header is sticky inside a scrolling
              container, so a translucent background would let scrolled-under rows show
              through and visually collide with the header text. */}
          <thead className="bg-muted text-muted-foreground text-xs uppercase tracking-wide font-medium sticky top-0">
            <tr>
              <th className="text-left px-4 py-2" title={timeLabel === "Time" ? undefined : "This device sends no time of its own; this is when each reading arrived."}>
                {timeLabel}
              </th>
              {series.map((key) => (
                <th key={key} className="text-left px-4 py-2">
                  {key}
                </th>
              ))}
              {inlineKeys.map((key) => (
                <th
                  key={`derived-${key}`}
                  className="text-left px-4 py-2 text-primary"
                  title="Calculated from measured telemetry — shown in black where the device measured it itself"
                >
                  ƒ {key}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {periodKeys.length === 0
              ? rows.map((p, i) => <ReadingRow key={`${p.timestamp}-${i}`} point={p} />)
              : periods.map((period, index) => {
                  // The period before this one in time ends where this one begins.
                  const previous = periods[index + 1]?.result;
                  const id = period.result?.timestamp ?? "open";
                  const isCollapsed = collapsed[id] ?? false;
                  const endsAt = period.result ? new Date(period.result.timestamp) : null;
                  const startsAt = previous ? new Date(previous.timestamp) : null;

                  return (
                    <Fragment key={id}>
                      <tr
                        className="bg-primary/10 cursor-pointer select-none"
                        onClick={() => setCollapsed((c) => ({ ...c, [id]: !isCollapsed }))}
                      >
                        <td colSpan={columnCount} className="px-4 py-2">
                          <div className="flex items-center gap-3 flex-wrap">
                            <span className="text-muted-foreground text-xs w-3">{isCollapsed ? "▶" : "▼"}</span>
                            {endsAt ? (
                              <>
                                <span className="text-xs text-muted-foreground whitespace-nowrap">
                                  {startsAt ? `${startsAt.toLocaleTimeString()} – ${endsAt.toLocaleTimeString()}` : `ending ${endsAt.toLocaleString()}`}
                                </span>
                                {periodKeys.map((key) =>
                                  period.result?.derived?.[key] !== undefined ? (
                                    <span key={key} className="text-primary tabular-nums font-medium whitespace-nowrap">
                                      ƒ {key} {formatMeasurement(period.result.derived[key])}
                                    </span>
                                  ) : null,
                                )}
                              </>
                            ) : (
                              <span className="text-xs text-muted-foreground">Period in progress — no value until it closes</span>
                            )}
                            <span className="text-xs text-muted-foreground ml-auto whitespace-nowrap">
                              {period.readings.length} {period.readings.length === 1 ? "reading" : "readings"}
                            </span>
                          </div>
                        </td>
                      </tr>
                      {!isCollapsed && period.readings.map((p, i) => <ReadingRow key={`${p.timestamp}-${i}`} point={p} />)}
                    </Fragment>
                  );
                })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
