"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { TelemetryApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import type { CalculationUpdatedEvent, TelemetryPointDto, TelemetryRange } from "@/types/api";

// Memory backstop for a very chatty device; far above what any range's
// history holds at normal reporting rates.
const MAX_POINTS = 20_000;

/**
 * Inserts or patches the point at `timestamp`, keeping the series in time
 * order. A reading and the value calculated from it are pushed as two
 * realtime events — whichever lands second must merge into the first rather
 * than add a duplicate row.
 *
 * The second event is not always the later time: a windowed calculation (an
 * Leq, a time-weighted average) is stamped at the end of the period it covers,
 * which is behind the reading that closed that period. So a point can arrive
 * belonging before the newest one, and appending it would put the table and
 * chart out of order.
 */
function upsertPoint(
  prev: TelemetryPointDto[],
  timestamp: string,
  windowMs: number | null,
  patch: (existing: TelemetryPointDto) => TelemetryPointDto,
): TelemetryPointDto[] {
  const target = new Date(timestamp).getTime();

  // Where this timestamp belongs among the points already held.
  let lo = 0;
  let hi = prev.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (new Date(prev[mid].timestamp).getTime() < target) lo = mid + 1;
    else hi = mid;
  }

  if (lo < prev.length && new Date(prev[lo].timestamp).getTime() === target) {
    const next = [...prev];
    next[lo] = patch(prev[lo]);
    return next;
  }

  const merged = [...prev.slice(0, lo), patch({ timestamp, fields: {}, derived: null }), ...prev.slice(lo)];

  // A new point slides the window forward by the time it adds: points older
  // than the span the history fetch covered drop off. (Trimming to a fixed
  // count instead cut an hour of history down to the last few minutes as soon
  // as the first live reading arrived.) The cutoff follows the newest point
  // held, not the one just inserted, so a value stamped into the past cannot
  // drag the window backwards. Without a span yet — no history — the window
  // just grows.
  let first = 0;
  if (windowMs !== null) {
    const cutoff = new Date(merged[merged.length - 1].timestamp).getTime() - windowMs;
    while (first < merged.length && new Date(merged[first].timestamp).getTime() < cutoff) first++;
  }
  first = Math.max(first, merged.length - MAX_POINTS);
  return first > 0 ? merged.slice(first) : merged;
}

/**
 * One-time REST fetch for whatever history already exists, then every later
 * point arrives purely via the already-open SignalR connection — no polling.
 * Measured values arrive as TelemetryUpdated; values the Calculations module
 * derives from them arrive as CalculationUpdated and stay in `derived`, so
 * charts and tables can always tell the two apart.
 */
export function useTelemetryHistory(deviceId: string) {
  const [range, setRange] = useState<TelemetryRange>("OneHour");
  const [points, setPoints] = useState<TelemetryPointDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const windowMsRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    TelemetryApi.history(deviceId, range)
      .then((data) => {
        if (cancelled) return;
        // The span the server returned for this range is the window live
        // updates keep — taken from the data, so it always matches the range.
        windowMsRef.current = data.length > 1 ? new Date(data[data.length - 1].timestamp).getTime() - new Date(data[0].timestamp).getTime() : null;
        setPoints(data);
      })
      .catch(() => !cancelled && setError("Failed to load telemetry history"))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [deviceId, range]);

  useRealtime({
    TelemetryUpdated: (payload) => {
      const p = payload as { deviceId: string } & TelemetryPointDto;
      if (p.deviceId !== deviceId) return;
      setPoints((prev) => upsertPoint(prev, p.timestamp, windowMsRef.current, (existing) => ({ ...existing, timestamp: p.timestamp, fields: p.fields ?? {} })));
    },
    CalculationUpdated: (payload) => {
      const u = payload as CalculationUpdatedEvent;
      if (u.deviceId !== deviceId) return;
      setPoints((prev) =>
        upsertPoint(prev, u.timestamp, windowMsRef.current, (existing) => ({ ...existing, derived: { ...(existing.derived ?? {}), [u.outputKey]: u.value } })),
      );
    },
  });

  // The field set isn't known up front — it's whatever the device's Device
  // Type defines — so series are derived from the keys that actually appear.
  // The `?? {}` guards a stale backend that sends points without `fields`.
  const derivedSeries = useMemo(() => {
    const keys = new Set<string>();
    for (const p of points) for (const key of Object.keys(p.derived ?? {})) keys.add(key);
    return Array.from(keys).sort();
  }, [points]);

  // A key that is calculated for some readings and measured in others (a
  // device that reports active power itself) is one quantity: it gets a
  // single calculated column, which shows the measured value where there is
  // one (see derivedOrMeasured) — not a second, mostly empty measured column.
  const series = useMemo(() => {
    const calculated = new Set(derivedSeries.map((k) => k.toLowerCase()));
    const keys = new Set<string>();
    for (const p of points) for (const key of Object.keys(p.fields ?? {})) if (!calculated.has(key.toLowerCase())) keys.add(key);
    return Array.from(keys).sort();
  }, [points, derivedSeries]);

  return { range, setRange, points, series, derivedSeries, loading, error };
}

/**
 * Whether a point is the result of a period rather than a reading.
 *
 * A windowed calculation (an Leq, a time-weighted average) publishes one value
 * per period, stamped at the period's end, so it arrives in the same series as
 * the readings but describes a span of them rather than a moment. It carries
 * calculated values and no measured ones, which is what tells the two apart —
 * a formula's value rides along on the reading it came from and stays an
 * ordinary point. The table groups by it and the chart scales and labels by
 * it, so both read the same signal from the same place.
 */
export function isPeriodResult(point: TelemetryPointDto): boolean {
  return Object.keys(point.derived ?? {}).length > 0 && Object.keys(point.fields ?? {}).length === 0;
}

/**
 * The calculated keys that only ever arrive as a period's result, and so
 * describe a span rather than an instant.
 */
export function periodKeysOf(points: TelemetryPointDto[], derivedSeries: string[]): string[] {
  const periodic = new Set<string>();
  for (const point of points) {
    if (isPeriodResult(point)) for (const key of Object.keys(point.derived ?? {})) periodic.add(key);
  }
  for (const point of points) {
    if (!isPeriodResult(point)) for (const key of Object.keys(point.derived ?? {})) periodic.delete(key);
  }
  return derivedSeries.filter((k) => periodic.has(k));
}

/**
 * A calculated column's value for one reading: what the device measured when
 * it sent the key itself (a measured value always wins — the formula isn't
 * evaluated for that reading), otherwise the calculated value.
 */
export function derivedOrMeasured(point: TelemetryPointDto, key: string): { value: number; measured: boolean } | undefined {
  const measured = point.fields?.[key];
  if (measured !== undefined) return { value: measured, measured: true };
  const calculated = point.derived?.[key];
  return calculated !== undefined ? { value: calculated, measured: false } : undefined;
}
