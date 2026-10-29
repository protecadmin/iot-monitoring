"use client";

import { useMemo, useState } from "react";
import { useApi } from "@/hooks/useApi";
import { AlertsApi, DeviceTypesApi, DevicesApi, TelemetryApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { levelOfRange, resolveRange } from "@/components/digital-twin/limits";
import type { Level, Limits, Range } from "@/components/digital-twin/types";
import type { AlertRuleDto, DashboardWidget, DeviceDto, DeviceTypeParameterDto } from "@/types/api";

/**
 * Limits to fall back on when a device has no alert rule for the reading —
 * the same standards the default rules use (ISO 10816, 85/90 dB(A), ISO 7243).
 * Real limits always come from the device's alert rules.
 */
const DEFAULT_LIMITS: Record<string, Limits> = {
  vibration: { warning: 4.5, critical: 7.1 },
  noiseLevel: { warning: 85, critical: 90 },
  wbgt: { warning: 30, critical: 32 },
};

/** One device's reading shown as a card (every widget but Consumption). */
export interface WidgetItem {
  kind: Exclude<DashboardWidget, "Consumption">;
  device: DeviceDto;
  parameter: DeviceTypeParameterDto;
  typeName: string;
  value: number | undefined;
  /** Last hour, oldest first. */
  trend: number[];
  /** From the device's alert rules: "greater than" rules are the high side, "less than" the low. */
  limits: Range | undefined;
  level: Level;
}

/**
 * Everything the dashboard widgets show, loaded once: which device gets
 * which widget (from its type's parameters), live values kept current by
 * realtime pushes, last-hour trends and limits from each device's alert rules.
 */
export function useWidgetData(tenantId: string | undefined, siteId: string | undefined) {
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId, pageSize: 200 }), [tenantId]);
  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  const latestQuery = useApi(() => TelemetryApi.latest(tenantId), [tenantId]);

  const devices = useMemo(
    () => (devicesQuery.data?.items ?? []).filter((d) => !siteId || d.siteId === siteId),
    [devicesQuery.data, siteId],
  );
  const typeById = useMemo(() => new Map((typesQuery.data ?? []).map((t) => [t.id, t])), [typesQuery.data]);
  const widgetParams = (device: DeviceDto) => (typeById.get(device.deviceTypeId)?.parameters ?? []).filter((p) => p.dashboardWidget);

  // Devices that need a trend. Consumption is reporting, not monitoring — it
  // lives in Reports, so it gets no card here.
  const cardDevices = devices.filter((d) => widgetParams(d).some((p) => p.dashboardWidget !== "Consumption"));

  // Limits per device come from the rules it is evaluated against, so a card
  // and its alarms always agree. A SuperAdmin viewing all tenants needs each one's.
  const tenantIds = [...new Set(devices.map((d) => d.tenantId))].sort();
  const rulesQuery = useApi(
    () => Promise.all(tenantIds.map((id) => AlertsApi.effectiveRules(id))).then((lists) => lists.flat()),
    [tenantIds.join(",")],
  );
  const historyQuery = useApi(
    () => Promise.all(cardDevices.map(async (d) => [d.id, await TelemetryApi.history(d.id, "OneHour")] as const)).then((rows) => new Map(rows)),
    [cardDevices.map((d) => d.id).join(",")],
  );
  // Live values arriving after the first load, per device and key.
  const [live, setLive] = useState<Record<string, Record<string, number>>>({});
  const [liveTrend, setLiveTrend] = useState<Record<string, Record<string, number[]>>>({});
  function push(deviceId: string, values: Record<string, number>) {
    setLive((prev) => ({ ...prev, [deviceId]: { ...prev[deviceId], ...values } }));
    setLiveTrend((prev) => {
      const forDevice = { ...prev[deviceId] };
      for (const [k, v] of Object.entries(values)) forDevice[k] = [...(forDevice[k] ?? []), v].slice(-120);
      return { ...prev, [deviceId]: forDevice };
    });
  }
  useRealtime({
    TelemetryUpdated: (payload) => {
      const p = payload as { deviceId: string; fields?: Record<string, number> };
      if (p.fields) push(p.deviceId, p.fields);
    },
    CalculationUpdated: (payload) => {
      const u = payload as { deviceId: string; outputKey: string; value: number };
      push(u.deviceId, { [u.outputKey]: u.value });
    },
  });

  const items = useMemo(() => {
    const rules: AlertRuleDto[] = rulesQuery.data ?? [];
    const result: WidgetItem[] = [];
    for (const device of devices) {
      const type = typeById.get(device.deviceTypeId);
      const latest = (latestQuery.data ?? []).find((l) => l.deviceId === device.id);
      const history = historyQuery.data?.get(device.id) ?? [];
      for (const p of widgetParams(device)) {
        if (p.dashboardWidget === "Consumption" || !p.dashboardWidget) continue;
        const measured = latest?.point?.fields?.[p.key];
        const calculated = latest?.calculated?.find((c) => c.outputKey === p.key)?.value;
        const value = live[device.id]?.[p.key] ?? measured ?? calculated;
        const trend = [
          ...history.map((pt) => pt.fields?.[p.key] ?? pt.derived?.[p.key]).filter((v): v is number => v != null),
          ...(liveTrend[device.id]?.[p.key] ?? []),
        ].slice(-120);
        const limits = resolveRange({ key: p.key, label: p.label, unit: p.unit ?? "", defaultLimits: DEFAULT_LIMITS[p.key] }, rules, device);
        result.push({ kind: p.dashboardWidget, device, parameter: p, typeName: type?.name ?? "", value, trend, limits, level: levelOfRange(value, limits) });
      }
    }
    return result;
    // widgetParams reads typeById, already a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [devices, typeById, latestQuery.data, historyQuery.data, rulesQuery.data, live, liveTrend]);

  return {
    items,
    loading: devicesQuery.loading || typesQuery.loading,
    error: devicesQuery.error ?? typesQuery.error,
  };
}
