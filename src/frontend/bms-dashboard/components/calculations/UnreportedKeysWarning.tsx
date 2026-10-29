"use client";

import { useMemo } from "react";
import { AlertTriangle } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { DevicesApi, TelemetryApi } from "@/lib/api/resources";

/**
 * Warns when a calculation reads a parameter that no device in its scope has
 * reported in its latest reading. The device type may define the key while
 * its devices never send it (an Energy Meter with a "temperature" parameter)
 * — the formula is valid, but would never produce a value.
 */
export function UnreportedKeysWarning({
  keys,
  tenantId,
  deviceTypeId,
  siteId,
  deviceId,
}: {
  /** Parameters the calculation reads. */
  keys: string[];
  tenantId?: string;
  deviceTypeId?: string;
  siteId?: string;
  deviceId?: string;
}) {
  const devicesQuery = useApi(
    () => (deviceTypeId || deviceId ? DevicesApi.list({ tenantId, deviceTypeId, siteId, pageSize: 200 }) : Promise.resolve(undefined)),
    [tenantId, deviceTypeId, siteId, deviceId],
  );
  const latestQuery = useApi(() => TelemetryApi.latest(tenantId, siteId), [tenantId, siteId]);

  const inScope = useMemo(
    () => (devicesQuery.data?.items ?? []).filter((d) => !deviceId || d.id === deviceId),
    [devicesQuery.data, deviceId],
  );

  const unreported = useMemo(() => {
    if (!latestQuery.data || inScope.length === 0) return [];
    const ids = new Set(inScope.map((d) => d.id));
    const reported = new Set<string>();
    for (const l of latestQuery.data) if (ids.has(l.deviceId)) for (const k of Object.keys(l.point?.fields ?? {})) reported.add(k.toLowerCase());
    return keys.filter((k) => !reported.has(k.toLowerCase()));
  }, [latestQuery.data, inScope, keys]);

  if (!devicesQuery.data || !latestQuery.data || keys.length === 0) return null;

  if (inScope.length === 0)
    return <p className="text-xs text-muted-foreground">No device is in this scope yet — the calculation starts once one reports.</p>;
  if (unreported.length === 0) return null;

  return (
    <p className="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
      <AlertTriangle className="h-3.5 w-3.5 mt-px shrink-0" />
      <span>
        No device in scope has reported <span className="font-mono">{unreported.join(", ")}</span> in its latest reading, so this calculation
        won&apos;t produce a value until one does. Check which keys the gateway sends.
      </span>
    </p>
  );
}
