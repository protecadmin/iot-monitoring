"use client";

import { Suspense, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import Link from "next/link";
import { Bell, ListChecks } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { AlertsApi, DevicesApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { ErrorState, EmptyState, StatCard, StatusBadge, TableSkeleton } from "@/components/ui/States";
import { StandardRulesSection } from "@/components/alerts/StandardRulesSection";
import type { AlertDto } from "@/types/api";

const STATUSES = ["", "Open", "Acknowledged", "Resolved"];
const SEVERITIES = ["", "Info", "Warning", "Critical"];

export default function AlertsPage() {
  return (
    <Suspense fallback={<TableSkeleton />}>
      <Alerts />
    </Suspense>
  );
}

function Alerts() {
  // ?status= and ?severity= preselect the filters — the dashboard's tiles link here.
  const params = useSearchParams();
  const { effectiveTenantId } = useTenantScope();
  const can = useCan();
  const canHandle = can("alerts.handle");
  const canResolve = can("alerts.resolve");
  const canEditRules = can("alertRules.edit");
  const canManageGlobal = can("globalAlertRules.manage");
  const [status, setStatus] = useState(() => params.get("status") ?? "Open");
  const [severity, setSeverity] = useState(() => params.get("severity") ?? "");
  const query = useApi(
    () => AlertsApi.list({ tenantId: effectiveTenantId, status: status || undefined, severity: severity || undefined, pageSize: 100 }),
    [effectiveTenantId, status, severity],
  );

  // Alerts pushed live since the last load. Once a reload returns the same
  // alert, the server's copy is shown and the pushed one is dropped.
  const [extra, setExtra] = useState<AlertDto[]>([]);
  useRealtime({
    AlertCreated: (payload) => setExtra((prev) => [payload as AlertDto, ...prev.filter((a) => a.id !== (payload as AlertDto).id)]),
    // Alerts close by themselves when a device recovers — reload so a
    // resolved one leaves the Open view (and the counts) straight away.
    AlertResolved: (payload) => {
      setExtra((prev) => prev.filter((a) => a.id !== (payload as AlertDto).id));
      query.refetch();
      summaryQuery.refetch();
    },
  });

  const loaded = query.data?.items ?? [];
  const loadedIds = new Set(loaded.map((a) => a.id));
  const items = [
    ...extra.filter((a) => !loadedIds.has(a.id) && (!status || a.status === status) && (!severity || a.severity === severity)),
    ...loaded,
  ];

  // Independent of the table's active filters, so the KPI row above always
  // reflects the whole fleet rather than whatever status/severity is
  // currently selected.
  const summaryQuery = useApi(() => AlertsApi.list({ tenantId: effectiveTenantId, pageSize: 500 }), [effectiveTenantId]);
  const summary = summaryQuery.data?.items ?? [];
  const openCount = summary.filter((a) => a.status !== "Resolved").length;
  const criticalCount = summary.filter((a) => a.severity === "Critical" && a.status !== "Resolved").length;
  const warningCount = summary.filter((a) => a.severity === "Warning" && a.status !== "Resolved").length;
  const resolvedCount = summary.filter((a) => a.status === "Resolved").length;

  // AlertDto only carries deviceId (a raw GUID) — resolve it against the
  // device list so the table shows a name instead of an unreadable ID.
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);
  const deviceById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of devicesQuery.data?.items ?? []) map.set(d.id, d.name);
    return map;
  }, [devicesQuery.data]);

  async function act(id: string, action: "acknowledge" | "resolve") {
    if (action === "acknowledge") await AlertsApi.acknowledge(id);
    else await AlertsApi.resolve(id);
    setExtra((prev) => prev.filter((a) => a.id !== id));
    query.refetch();
  }

  // Rule management needs one specific tenant — the backend's rules
  // endpoints require a tenantId, unlike the alerts list which tolerates
  // "all tenants" for a SuperAdmin.

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold text-foreground">Alerts</h1>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatCard label="Active Alerts" value={openCount} icon={Bell} />
        <StatCard label="Critical" value={criticalCount} tone="critical" />
        <StatCard label="Warning" value={warningCount} tone="warning" />
        <StatCard label="Resolved" value={resolvedCount} tone="success" icon={ListChecks} />
      </div>

      <div className="flex gap-2">
        <select value={status} onChange={(e) => setStatus(e.target.value)} className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5">
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s || "All statuses"}
            </option>
          ))}
        </select>
        <select value={severity} onChange={(e) => setSeverity(e.target.value)} className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5">
          {SEVERITIES.map((s) => (
            <option key={s} value={s}>
              {s || "All severities"}
            </option>
          ))}
        </select>
      </div>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <TableSkeleton cols={canHandle ? 7 : 6} />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && items.length === 0 && (
        <EmptyState message="No active alerts" hint="All monitored devices are currently operating within configured thresholds." icon={Bell} />
      )}

      {items.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Severity</th>
                <th className="text-left px-4 py-2">Device</th>
                <th className="text-left px-4 py-2">Parameter</th>
                <th className="text-left px-4 py-2">Value / Threshold</th>
                <th className="text-left px-4 py-2">Triggered</th>
                <th className="text-left px-4 py-2">Status</th>
                {canHandle && <th className="text-left px-4 py-2">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {items.map((a) => (
                <tr key={a.id}>
                  <td className="px-4 py-2">
                    <StatusBadge status={a.severity} />
                  </td>
                  <td className="px-4 py-2">
                    <Link href={`/devices/${a.deviceId}`} className="text-primary hover:underline">
                      {deviceById.get(a.deviceId) ?? a.deviceId.slice(0, 8)}
                    </Link>
                  </td>
                  <td className="px-4 py-2">{a.parameter}</td>
                  <td className="px-4 py-2 text-muted-foreground">
                    {a.currentValue?.toFixed(2)} / {a.threshold?.toFixed(2)}
                  </td>
                  <td className="px-4 py-2 text-muted-foreground text-xs">{new Date(a.triggeredAt).toLocaleString()}</td>
                  <td className="px-4 py-2">
                    <StatusBadge status={a.status} />
                  </td>
                  {canHandle && (
                    <td className="px-4 py-2 space-x-2">
                      {a.status === "Open" && (
                        <button onClick={() => act(a.id, "acknowledge")} className="text-xs text-primary hover:underline">
                          Acknowledge
                        </button>
                      )}
                      {canResolve && a.status !== "Resolved" && (
                        <button onClick={() => act(a.id, "resolve")} className="text-xs text-success hover:underline">
                          Resolve
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="pt-4">
        <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
          <h2 className="text-lg font-semibold text-foreground">Alert rules</h2>
          {canManageGlobal && (
            <Link href="/alert-rules" className="text-sm text-primary hover:underline">
              Manage rules for all companies →
            </Link>
          )}
        </div>

        {!effectiveTenantId ? (
          <p className="text-sm text-muted-foreground">Pick a specific tenant (top of the sidebar) to view{canEditRules ? " or change" : ""} its alert rules.</p>
        ) : (
          <StandardRulesSection tenantId={effectiveTenantId} canEdit={canEditRules} />
        )}
      </div>
    </div>
  );
}
