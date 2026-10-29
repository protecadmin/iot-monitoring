"use client";

import { Suspense, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ArrowLeft, Box } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { AlertsApi, DeviceTypesApi, DevicesApi, TelemetryApi } from "@/lib/api/resources";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { resolveTwin } from "@/components/digital-twin/registry";
import { healthOf, levelOf, resolveLimits } from "@/components/digital-twin/limits";
import { DigitalTwinView } from "@/components/digital-twin/DigitalTwinView";
import { TwinTile } from "@/components/digital-twin/TwinTile";
import type { AlertRuleDto, DeviceDto } from "@/types/api";

export default function DigitalTwinPage() {
  return (
    <Suspense fallback={<LoadingState />}>
      <DigitalTwins />
    </Suspense>
  );
}

function DigitalTwins() {
  const selectedId = useSearchParams().get("device");
  const { effectiveTenantId } = useTenantScope();

  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);
  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  const latestQuery = useApi(() => TelemetryApi.latest(effectiveTenantId), [effectiveTenantId]);

  const typeName = useMemo(() => new Map((typesQuery.data ?? []).map((t) => [t.id, t.name])), [typesQuery.data]);
  // Devices whose twin settings were just saved here, so the change shows without a reload.
  const [saved, setSaved] = useState<Record<string, DeviceDto>>({});
  const twins = useMemo(
    () =>
      (devicesQuery.data?.items ?? [])
        .map((d) => saved[d.id] ?? d)
        .map((device) => ({ device, twin: resolveTwin(device, typeName.get(device.deviceTypeId)) }))
        .filter((t): t is { device: DeviceDto; twin: NonNullable<(typeof t)["twin"]> } => !!t.twin),
    [devicesQuery.data, typeName, saved],
  );

  // Limits come from the alert rules each device is evaluated against —
  // standard rules with company/device changes applied (several tenants when
  // a SuperAdmin views all).
  const tenantIds = useMemo(() => [...new Set(twins.map((t) => t.device.tenantId))].sort(), [twins]);
  const rulesQuery = useApi(
    () => Promise.all(tenantIds.map((id) => AlertsApi.effectiveRules(id))).then((lists) => lists.flat()),
    [tenantIds.join(",")],
  );

  // Latest headline values and statuses, kept current by realtime pushes.
  const [liveValues, setLiveValues] = useState<Record<string, Record<string, number>>>({});
  const [liveStatus, setLiveStatus] = useState<Record<string, string>>({});
  useRealtime({
    TelemetryUpdated: (payload) => {
      const p = payload as { deviceId: string; fields?: Record<string, number> };
      if (p.fields) setLiveValues((prev) => ({ ...prev, [p.deviceId]: { ...(prev[p.deviceId] ?? {}), ...p.fields } }));
    },
    DeviceStatusChanged: (payload) => {
      const p = payload as { id: string; status: string };
      setLiveStatus((prev) => ({ ...prev, [p.id]: p.status }));
    },
  });

  const fieldsFor = (deviceId: string) => ({
    ...((latestQuery.data ?? []).find((l) => l.deviceId === deviceId)?.point?.fields ?? {}),
    ...(liveValues[deviceId] ?? {}),
  });

  if ((devicesQuery.loading && !devicesQuery.data) || (typesQuery.loading && !typesQuery.data)) return <LoadingState />;
  if (devicesQuery.error) return <ErrorState message={devicesQuery.error} />;

  const selected = selectedId ? twins.find((t) => t.device.id === selectedId) : undefined;
  if (selectedId) {
    return (
      <div className="space-y-3">
        <Link href="/digital-twin" className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-3.5 w-3.5" /> All digital twins
        </Link>
        {selected ? (
          <DigitalTwinView
            device={{ ...selected.device, status: liveStatus[selected.device.id] ?? selected.device.status }}
            twin={selected.twin}
            deviceTypeName={typeName.get(selected.device.deviceTypeId)}
            onDeviceChange={(d) => setSaved((prev) => ({ ...prev, [d.id]: d }))}
          />
        ) : (
          <EmptyState message="No digital twin for this device" hint="Twins exist for vibration, WBGT and noise devices." icon={Box} />
        )}
      </div>
    );
  }

  const rules: AlertRuleDto[] = rulesQuery.data ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2">
        <Box className="h-5 w-5 text-muted-foreground mt-0.5" />
        <div>
          <h1 className="text-xl font-semibold text-foreground">Digital Twin</h1>
          <p className="text-xs text-muted-foreground mt-0.5">Live picture of each monitored equipment. Colours follow the alert limits: green normal, amber warning, red critical.</p>
        </div>
      </div>

      {twins.length === 0 ? (
        <EmptyState message="No equipment with a digital twin yet" hint="Twins appear for Vibration, WBGT and Noise sensor devices." icon={Box} />
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {twins.map(({ device, twin }) => {
            const fields = fieldsFor(device.id);
            const value = fields[twin.profile.primary.key];
            const limits = resolveLimits(twin.profile.primary, rules, device);
            const health = healthOf(liveStatus[device.id] ?? device.status, levelOf(value, limits));
            return <TwinTile key={device.id} device={device} twin={twin} value={value} health={health} />;
          })}
        </div>
      )}
    </div>
  );
}
