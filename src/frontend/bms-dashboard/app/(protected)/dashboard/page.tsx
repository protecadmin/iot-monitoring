"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { AlertTriangle, Bell, Building2, Cpu, MapPin, PowerOff, Wifi, type LucideIcon } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { useCan } from "@/lib/auth/permissions";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { AlertsApi, DevicesApi, SitesApi, TenantsApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { LoadingState, ErrorState, LiveIndicator } from "@/components/ui/States";
import { TelemetryChart } from "@/components/charts/TelemetryChart";
import { WidgetDashboard } from "@/components/dashboard/WidgetDashboard";
import { LiveAlerts } from "@/components/dashboard/LiveAlerts";
import { DeviceStatusStrip } from "@/components/dashboard/DeviceStatusStrip";
import { ApiError } from "@/lib/api/client";
import type { AlertDto, DeviceDto } from "@/types/api";

/**
 * The live monitoring screen: what is wrong now (alerts), the state of every
 * device, the readings people act on, and one device's recent telemetry.
 * Usage and consumption are reporting, so they live in Reports, not here.
 */
export default function DashboardPage() {
  const { user } = useAuth();
  const canHandle = useCan()("alerts.handle");
  const { effectiveTenantId } = useTenantScope();
  const isSuperAdmin = user?.role === "SuperAdmin";

  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);
  // Unresolved = Open + Acknowledged: an acknowledged alert is seen, not fixed.
  const alertsQuery = useApi(
    () =>
      Promise.all([
        AlertsApi.list({ tenantId: effectiveTenantId, status: "Open", pageSize: 200 }),
        AlertsApi.list({ tenantId: effectiveTenantId, status: "Acknowledged", pageSize: 200 }),
      ]).then(([open, acked]) => ({ items: [...open.items, ...acked.items] })),
    [effectiveTenantId],
  );
  const tenantsQuery = useApi(() => (isSuperAdmin ? TenantsApi.list() : Promise.resolve(undefined)), [isSuperAdmin]);
  const sitesQuery = useApi(() => SitesApi.list(effectiveTenantId), [effectiveTenantId]);

  const [liveDevices, setLiveDevices] = useState<DeviceDto[] | undefined>(undefined);
  const [liveAlerts, setLiveAlerts] = useState<AlertDto[] | undefined>(undefined);
  const devices = liveDevices ?? devicesQuery.data?.items;
  const alerts = liveAlerts ?? alertsQuery.data?.items;

  useRealtime({
    DeviceStatusChanged: (payload) => {
      const p = payload as { id: string; status: string; lastSeenAt: string | null; lastTelemetryAt: string | null; lastHeartbeatAt: string | null };
      setLiveDevices((prev) => {
        const base = prev ?? devicesQuery.data?.items ?? [];
        return base.map((d) => (d.id === p.id ? { ...d, status: p.status, lastSeenAt: p.lastSeenAt, lastTelemetryAt: p.lastTelemetryAt, lastHeartbeatAt: p.lastHeartbeatAt } : d));
      });
    },
    AlertCreated: (payload) => {
      const alert = payload as AlertDto;
      // The same alert can be pushed more than once; keep a single row.
      setLiveAlerts((prev) => [alert, ...(prev ?? alertsQuery.data?.items ?? []).filter((a) => a.id !== alert.id)]);
    },
    AlertResolved: (payload) => {
      const alert = payload as AlertDto;
      setLiveAlerts((prev) => (prev ?? alertsQuery.data?.items ?? []).filter((a) => a.id !== alert.id));
    },
  });

  const [siteId, setSiteId] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  // Everything below follows the site filter.
  const scopedDevices = useMemo(() => (devices ?? []).filter((d) => !siteId || d.siteId === siteId), [devices, siteId]);
  const deviceById = useMemo(() => new Map(scopedDevices.map((d) => [d.id, d])), [scopedDevices]);
  const scopedAlerts = useMemo(() => (alerts ?? []).filter((a) => deviceById.has(a.deviceId)), [alerts, deviceById]);

  const onlineCount = scopedDevices.filter((d) => d.status === "Online").length;
  const offlineCount = scopedDevices.filter((d) => d.status === "Offline").length;
  const criticalCount = scopedAlerts.filter((a) => a.severity === "Critical").length;

  async function acknowledge(alert: AlertDto) {
    setActionError(null);
    try {
      const updated = await AlertsApi.acknowledge(alert.id);
      setLiveAlerts((prev) => (prev ?? alertsQuery.data?.items ?? []).map((a) => (a.id === alert.id ? { ...a, ...updated, status: "Acknowledged" } : a)));
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't acknowledge the alert");
    }
  }

  const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");
  // The chart opens on a device worth looking at: one with an open alert
  // (critical first) that is reporting, else any reporting device — never an
  // offline one with nothing to draw.
  const defaultDeviceId = useMemo(() => {
    const online = new Set(scopedDevices.filter((d) => d.status === "Online").map((d) => d.id));
    const alerted = [...scopedAlerts].sort((a, b) => Number(b.severity === "Critical") - Number(a.severity === "Critical"));
    return alerted.find((a) => online.has(a.deviceId))?.deviceId ?? scopedDevices.find((d) => online.has(d.id))?.id ?? scopedDevices[0]?.id ?? "";
  }, [scopedDevices, scopedAlerts]);
  const activeDeviceId = (selectedDeviceId && deviceById.has(selectedDeviceId) ? selectedDeviceId : "") || defaultDeviceId;

  if (devicesQuery.loading) return <LoadingState label="Loading dashboard..." />;
  if (devicesQuery.error) return <ErrorState message={devicesQuery.error} />;

  const sites = sitesQuery.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-baseline gap-3">
          <h1 className="text-lg font-semibold text-foreground">Dashboard</h1>
          <LiveIndicator />
        </div>
        {sites.length > 1 && (
          <select
            value={siteId}
            onChange={(e) => setSiteId(e.target.value)}
            className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1"
            aria-label="Site"
          >
            <option value="">All sites</option>
            {sites.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        )}
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2">
        {isSuperAdmin && <Stat label="Tenants" value={tenantsQuery.data?.totalCount ?? "—"} icon={Building2} href="/tenants" />}
        <Stat label="Sites" value={sitesQuery.data?.totalCount ?? "—"} icon={MapPin} href="/sites" />
        <Stat label="Devices" value={scopedDevices.length} icon={Cpu} href="/devices" />
        <Stat label="Online" value={onlineCount} tone="text-emerald-600 dark:text-emerald-400" icon={Wifi} href="/devices?status=Online" />
        <Stat label="Offline" value={offlineCount} tone="text-amber-600 dark:text-amber-400" icon={PowerOff} href="/devices?status=Offline" />
        <Stat label="Active alerts" value={scopedAlerts.length} tone="text-amber-600 dark:text-amber-400" icon={Bell} href="/alerts" />
        <Stat label="Critical" value={criticalCount} tone="text-red-600 dark:text-red-400" icon={AlertTriangle} href="/alerts?severity=Critical" />
      </div>

      <DeviceStatusStrip devices={scopedDevices} alerts={scopedAlerts} />

      <WidgetDashboard tenantId={effectiveTenantId} siteId={siteId} />

      {actionError && <p className="text-xs text-destructive">{actionError}</p>}
      <LiveAlerts alerts={scopedAlerts} devices={deviceById} canHandle={canHandle} onAcknowledge={acknowledge} />

      <section className="bg-card border border-border rounded-lg p-3 shadow-sm min-w-0">
        <div className="flex items-center justify-between gap-2 mb-2">
          <h2 className="text-sm font-semibold text-foreground">Telemetry</h2>
          <select
            value={activeDeviceId}
            onChange={(e) => setSelectedDeviceId(e.target.value)}
            className="text-xs border border-input bg-background text-foreground rounded-md px-2 py-1 max-w-[60%]"
          >
            {scopedDevices.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name} ({d.deviceCode}){d.status === "Offline" ? " · offline" : ""}
              </option>
            ))}
          </select>
        </div>
        {activeDeviceId ? <TelemetryChart deviceId={activeDeviceId} /> : <p className="text-sm text-muted-foreground">No devices yet.</p>}
      </section>
    </div>
  );
}

/** One compact figure in the summary row; opens the list behind the number. */
function Stat({ label, value, tone, icon: Icon, href }: { label: string; value: string | number; tone?: string; icon: LucideIcon; href: string }) {
  return (
    <Link
      href={href}
      className="bg-card border border-border rounded-lg px-3 py-2 shadow-sm flex items-center justify-between gap-2 hover:border-primary transition-colors"
    >
      <div className="min-w-0">
        <div className="text-[11px] text-muted-foreground truncate">{label}</div>
        <div className={`text-lg font-semibold leading-tight tabular-nums ${tone ?? "text-foreground"}`}>{value}</div>
      </div>
      <Icon className={`h-4 w-4 shrink-0 ${tone ?? "text-muted-foreground"}`} />
    </Link>
  );
}
