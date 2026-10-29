"use client";

import { Suspense, useMemo, useState, useSyncExternalStore } from "react";
import { useSearchParams } from "next/navigation";
import Link from "next/link";
import { Bell, BellOff, Cpu, LayoutGrid, Plus, Search, Table2 } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { AlertsApi, DeviceTypesApi, DevicesApi, SitesApi, TelemetryApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { ErrorState, EmptyState, StatusBadge, TableSkeleton } from "@/components/ui/States";
import { DeviceFormModal } from "@/components/devices/DeviceFormModal";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { formatMeasurement } from "@/lib/format";
import type { DeviceDto, DeviceTypeDto } from "@/types/api";

interface LiveDeviceStatus {
  status: string;
  lastSeenAt: string | null;
}

type View = "table" | "cards";
const VIEW_KEY = "bms.devices.view";
const VIEW_EVENT = "bms-devices-view";

// The chosen view is a per-viewer convenience kept in localStorage. Read
// through useSyncExternalStore so the server render ("table") and the first
// client render agree, then the saved choice applies.
function readView(): View {
  try {
    return window.localStorage.getItem(VIEW_KEY) === "cards" ? "cards" : "table";
  } catch {
    return "table";
  }
}
function subscribeView(onChange: () => void) {
  window.addEventListener(VIEW_EVENT, onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(VIEW_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}
function saveView(view: View) {
  try {
    window.localStorage.setItem(VIEW_KEY, view);
  } catch {
    // storage blocked — the choice just isn't remembered
  }
  window.dispatchEvent(new Event(VIEW_EVENT));
}

/** A device's latest values, measured first then calculated, keyed like its type's parameters. */
type Latest = Record<string, number>;

/** The latest values worth a glance, in the type's own parameter order, with units: "12.4 A · 231 V". */
function readingSummary(latest: Latest | undefined, type: DeviceTypeDto | undefined, limit: number) {
  if (!latest) return [];
  const order = type?.parameters ?? [];
  const entries = order.filter((p) => latest[p.key] !== undefined).map((p) => ({ key: p.key, label: p.label, value: latest[p.key], unit: p.unit }));
  // Keys the type doesn't list (shouldn't happen — the API filters them) still show, after the rest.
  for (const [key, value] of Object.entries(latest)) if (!order.some((p) => p.key === key)) entries.push({ key, label: key, value, unit: null });
  return entries.slice(0, limit);
}

function ReadingSummary({ latest, type, limit = 3 }: { latest: Latest | undefined; type: DeviceTypeDto | undefined; limit?: number }) {
  const all = readingSummary(latest, type, 99);
  if (all.length === 0) return <span className="text-muted-foreground">—</span>;
  const shown = all.slice(0, limit);
  const title = all.map((e) => `${e.label}: ${formatMeasurement(e.value)}${e.unit ? ` ${e.unit}` : ""}`).join("\n");
  return (
    <span className="tabular-nums whitespace-nowrap" title={title}>
      {shown.map((e) => `${formatMeasurement(e.value)}${e.unit ? ` ${e.unit}` : ""}`).join(" · ")}
      {all.length > shown.length && <span className="text-muted-foreground"> +{all.length - shown.length}</span>}
    </span>
  );
}

/**
 * Open-alert count, plus the device's alerts on/off switch. Off mutes it —
 * no new alerts from any rule, e.g. during maintenance; readings are still
 * stored and alerts already open stay open.
 */
function AlertCell({
  count,
  enabled,
  canToggle,
  onToggle,
}: {
  count: number;
  enabled: boolean;
  canToggle: boolean;
  onToggle: (enabled: boolean) => void;
}) {
  return (
    <span className="inline-flex items-center gap-2">
      {count > 0 ? (
        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-destructive/10 text-destructive text-xs font-medium" title={`${count} open alert${count === 1 ? "" : "s"}`}>
          <Bell className="h-3 w-3" />
          {count}
        </span>
      ) : (
        <span className="text-muted-foreground">—</span>
      )}
      {canToggle ? (
        <button
          onClick={(e) => {
            // In card view the cell sits inside the card's link.
            e.preventDefault();
            e.stopPropagation();
            onToggle(!enabled);
          }}
          aria-pressed={enabled}
          title={enabled ? "Alerts on — click to mute this device (e.g. maintenance)" : "Alerts muted — click to turn them back on"}
          className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-xs border ${
            enabled ? "border-border text-muted-foreground hover:text-foreground" : "border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-400"
          }`}
        >
          {enabled ? <Bell className="h-3 w-3" /> : <BellOff className="h-3 w-3" />}
          {enabled ? "On" : "Muted"}
        </button>
      ) : (
        !enabled && (
          <span className="inline-flex items-center gap-1 text-xs text-amber-700 dark:text-amber-400" title="Alerts muted for this device">
            <BellOff className="h-3 w-3" /> Muted
          </span>
        )
      )}
    </span>
  );
}

const STATUSES = ["", "Registered", "Provisioned", "Online", "Offline", "Warning", "Error", "Maintenance", "Decommissioned"];

export default function DevicesPage() {
  return (
    <Suspense fallback={<TableSkeleton />}>
      <Devices />
    </Suspense>
  );
}

function Devices() {
  const { effectiveTenantId } = useTenantScope();
  const [search, setSearch] = useState("");
  // ?status=Offline preselects the filter — the dashboard's tiles link here.
  const initialStatus = useSearchParams().get("status");
  const [status, setStatus] = useState(() => initialStatus ?? "");
  const [deviceTypeId, setDeviceTypeId] = useState("");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const query = useApi(
    () =>
      DevicesApi.list({
        tenantId: effectiveTenantId,
        search: search || undefined,
        status: status || undefined,
        deviceTypeId: deviceTypeId || undefined,
        page,
        pageSize,
      }),
    [effectiveTenantId, search, status, deviceTypeId, page],
  );

  // Device names are free text ("AHU-001 Supply Temp"), so the type is what
  // says what a device actually measures.
  const deviceTypesQuery = useApi(() => DeviceTypesApi.list(), []);
  const deviceTypeById = useMemo(() => {
    const map = new Map<string, DeviceTypeDto>();
    for (const t of deviceTypesQuery.data ?? []) map.set(t.id, t);
    return map;
  }, [deviceTypesQuery.data]);

  // Health at a glance: each device's latest values and open alerts. Loaded
  // once, then kept current by the realtime pushes below.
  const latestQuery = useApi(() => TelemetryApi.latest(effectiveTenantId), [effectiveTenantId]);
  const [liveLatest, setLiveLatest] = useState<Record<string, Latest>>({});
  const latestByDevice = useMemo(() => {
    const map: Record<string, Latest> = {};
    for (const l of latestQuery.data ?? []) {
      const values: Latest = {};
      for (const c of l.calculated ?? []) values[c.outputKey] = c.value;
      Object.assign(values, l.point?.fields ?? {}); // a measured value wins
      map[l.deviceId] = values;
    }
    for (const [id, values] of Object.entries(liveLatest)) map[id] = { ...map[id], ...values };
    return map;
  }, [latestQuery.data, liveLatest]);

  const openAlertsQuery = useApi(
    async () => {
      const [open, acknowledged] = await Promise.all([
        AlertsApi.list({ tenantId: effectiveTenantId, status: "Open", pageSize: 200 }),
        AlertsApi.list({ tenantId: effectiveTenantId, status: "Acknowledged", pageSize: 200 }),
      ]);
      const counts: Record<string, number> = {};
      for (const a of [...open.items, ...acknowledged.items]) counts[a.deviceId] = (counts[a.deviceId] ?? 0) + 1;
      return counts;
    },
    [effectiveTenantId],
  );
  const alertCount = (deviceId: string) => openAlertsQuery.data?.[deviceId] ?? 0;

  // Alerts on/off per device, shown at once and saved in the background.
  const [alertsOverride, setAlertsOverride] = useState<Record<string, boolean>>({});
  const [alertsError, setAlertsError] = useState<string | null>(null);
  const alertsOn = (d: DeviceDto) => alertsOverride[d.id] ?? d.alertsEnabled ?? true;
  async function toggleAlerts(d: DeviceDto, enabled: boolean) {
    setAlertsError(null);
    setAlertsOverride((prev) => ({ ...prev, [d.id]: enabled }));
    try {
      await DevicesApi.setAlerts(d.id, enabled);
    } catch {
      setAlertsOverride((prev) => ({ ...prev, [d.id]: !enabled }));
      setAlertsError(`Couldn't turn alerts ${enabled ? "on" : "off"} for ${d.name}.`);
    }
  }

  const view = useSyncExternalStore<View>(subscribeView, readView, () => "table");

  const sitesQuery = useApi(() => SitesApi.list(effectiveTenantId), [effectiveTenantId]);
  const siteById = useMemo(() => {
    const map = new Map<string, string>();
    for (const s of sitesQuery.data?.items ?? []) map.set(s.id, s.name);
    return map;
  }, [sitesQuery.data]);

  const totalPages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / pageSize)) : 1;

  // Without this, a device going Offline (the periodic sweep in
  // Bms.Ingestion) or coming back Online never shows up here until the
  // page is manually reloaded — this list otherwise only ever does the
  // initial REST fetch, unlike Dashboard/device-detail which already
  // listen for these events.
  const [liveStatus, setLiveStatus] = useState<Record<string, LiveDeviceStatus>>({});
  useRealtime({
    DeviceStatusChanged: (payload) => {
      const p = payload as { id: string; status: string; lastSeenAt: string | null };
      setLiveStatus((prev) => ({ ...prev, [p.id]: { status: p.status, lastSeenAt: p.lastSeenAt } }));
    },
    TelemetryUpdated: (payload) => {
      const p = payload as { deviceId: string; fields?: Record<string, number> };
      if (p.fields) setLiveLatest((prev) => ({ ...prev, [p.deviceId]: { ...prev[p.deviceId], ...p.fields } }));
    },
    CalculationUpdated: (payload) => {
      const u = payload as { deviceId: string; outputKey: string; value: number };
      // A formula only runs for readings that lack the key, so the newest
      // value is always the one to show.
      setLiveLatest((prev) => ({ ...prev, [u.deviceId]: { ...prev[u.deviceId], [u.outputKey]: u.value } }));
    },
    AlertCreated: () => openAlertsQuery.refetch(),
    AlertResolved: () => openAlertsQuery.refetch(),
  });

  const can = useCan();
  const canEdit = can("devices.edit");
  const canDelete = can("devices.delete");
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<DeviceDto | null>(null);
  const [deleting, setDeleting] = useState<DeviceDto | null>(null);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Devices</h1>
          <p className="text-sm text-muted-foreground">Every device registered across {effectiveTenantId ? "this tenant" : "all tenants"}.</p>
        </div>
        {canEdit && (
          <button
            onClick={() => setCreating(true)}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
          >
            <Plus className="h-4 w-4" /> New device
          </button>
        )}
      </div>

      <div className="flex flex-wrap gap-2">
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
          <input
            placeholder="Search by ID, name, or serial number"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            className="text-sm border border-input bg-background text-foreground rounded-md pl-8 pr-3 py-1.5 w-72"
          />
        </div>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
          className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5"
        >
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s || "All statuses"}
            </option>
          ))}
        </select>
        <select
          value={deviceTypeId}
          onChange={(e) => {
            setDeviceTypeId(e.target.value);
            setPage(1);
          }}
          className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5"
        >
          <option value="">All device types</option>
          {deviceTypesQuery.data?.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
        <div className="ml-auto flex rounded-md border border-input overflow-hidden" role="group" aria-label="View">
          {(
            [
              ["table", Table2, "Table"],
              ["cards", LayoutGrid, "Cards"],
            ] as const
          ).map(([value, Icon, label]) => (
            <button
              key={value}
              onClick={() => saveView(value)}
              aria-pressed={view === value}
              title={label}
              className={`flex items-center gap-1.5 px-2.5 py-1.5 text-sm ${view === value ? "bg-primary text-primary-foreground" : "bg-background text-muted-foreground hover:bg-muted"}`}
            >
              <Icon className="h-3.5 w-3.5" /> {label}
            </button>
          ))}
        </div>
      </div>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <TableSkeleton cols={canEdit ? 8 : 7} />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}
      {alertsError && <p className="text-xs text-destructive">{alertsError}</p>}
      {!query.loading && !query.error && query.data && query.data.items.length === 0 && (
        <EmptyState message="No devices match these filters." hint={canEdit ? "Try clearing a filter, or register a new device to get started." : "Try clearing a filter."} icon={Cpu} />
      )}

      {!query.loading && !query.error && query.data && query.data.items.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          {view === "table" ? (
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
                <tr>
                  <th className="text-left px-4 py-2">Device</th>
                  <th className="text-left px-4 py-2">Device type</th>
                  <th className="text-left px-4 py-2">Site</th>
                  <th className="text-left px-4 py-2">Status</th>
                  <th className="text-left px-4 py-2">Latest reading</th>
                  <th className="text-left px-4 py-2">Alerts</th>
                  <th className="text-left px-4 py-2">Last seen</th>
                  {canEdit && <th className="text-left px-4 py-2">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {query.data.items.map((d) => {
                  const live = liveStatus[d.id];
                  const status = live?.status ?? d.status;
                  const lastSeenAt = live?.lastSeenAt ?? d.lastSeenAt;
                  const type = deviceTypeById.get(d.deviceTypeId);
                  return (
                    <tr key={d.id} className="hover:bg-muted/50">
                      <td className="px-4 py-2">
                        <Link href={`/devices/${d.id}`} className="text-primary hover:underline font-medium">
                          {d.name}
                        </Link>
                        <div className="text-xs text-muted-foreground">{d.deviceCode}</div>
                      </td>
                      <td className="px-4 py-2 text-foreground whitespace-nowrap">{type?.name ?? "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground">{siteById.get(d.siteId) ?? d.siteId.slice(0, 8)}</td>
                      <td className="px-4 py-2">
                        <StatusBadge status={status} />
                      </td>
                      <td className="px-4 py-2 text-foreground">
                        <ReadingSummary latest={latestByDevice[d.id]} type={type} />
                      </td>
                      <td className="px-4 py-2">
                        <AlertCell count={alertCount(d.id)} enabled={alertsOn(d)} canToggle={canEdit} onToggle={(on) => toggleAlerts(d, on)} />
                      </td>
                      <td className="px-4 py-2 text-muted-foreground text-xs">{lastSeenAt ? new Date(lastSeenAt).toLocaleString() : "Never"}</td>
                      {canEdit && (
                        <td className="px-4 py-2 space-x-2 whitespace-nowrap">
                          <button onClick={() => setEditing(d)} className="text-xs text-primary hover:underline">
                            Edit
                          </button>
                          {canDelete && (
                            <button onClick={() => setDeleting(d)} className="text-xs text-destructive hover:underline">
                              Delete
                            </button>
                          )}
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          ) : (
            // Cards are for watching health: status and alerts stand out, with
            // the device's key live values. Editing stays on the table view
            // and the device page.
            <div className="grid gap-3 p-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {query.data.items.map((d) => {
                const live = liveStatus[d.id];
                const status = live?.status ?? d.status;
                const lastSeenAt = live?.lastSeenAt ?? d.lastSeenAt;
                const type = deviceTypeById.get(d.deviceTypeId);
                const alerts = alertCount(d.id);
                const values = readingSummary(latestByDevice[d.id], type, 4);
                return (
                  <Link
                    key={d.id}
                    href={`/devices/${d.id}`}
                    className={`block rounded-lg border bg-background p-3 hover:border-primary transition-colors ${alerts > 0 ? "border-destructive/50" : "border-border"}`}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="text-sm font-medium text-foreground truncate">{d.name}</div>
                        <div className="text-xs text-muted-foreground truncate">
                          {type?.name ?? "—"} · {siteById.get(d.siteId) ?? ""}
                        </div>
                      </div>
                      <StatusBadge status={status} />
                    </div>
                    <dl className="grid grid-cols-2 gap-x-3 gap-y-1 mt-3 min-h-[2.5rem]">
                      {values.length === 0 ? (
                        <dd className="col-span-2 text-xs text-muted-foreground">No readings yet</dd>
                      ) : (
                        values.map((v) => (
                          <div key={v.key} className="min-w-0">
                            <dt className="text-[10px] uppercase tracking-wide text-muted-foreground truncate">{v.label}</dt>
                            <dd className="text-sm text-foreground tabular-nums truncate">
                              {formatMeasurement(v.value)}
                              {v.unit && <span className="text-xs text-muted-foreground"> {v.unit}</span>}
                            </dd>
                          </div>
                        ))
                      )}
                    </dl>
                    <div className="flex items-center justify-between mt-3 text-xs text-muted-foreground">
                      <span>{lastSeenAt ? new Date(lastSeenAt).toLocaleString() : "Never seen"}</span>
                      <AlertCell count={alerts} enabled={alertsOn(d)} canToggle={canEdit} onToggle={(on) => toggleAlerts(d, on)} />
                    </div>
                  </Link>
                );
              })}
            </div>
          )}

          <div className="flex items-center justify-between px-4 py-3 border-t border-border text-sm">
            <span className="text-muted-foreground">
              Page {page} of {totalPages} ({query.data.totalCount} devices)
            </span>
            <div className="flex gap-2">
              <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="px-2 py-1 border border-input rounded disabled:opacity-40">
                Previous
              </button>
              <button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="px-2 py-1 border border-input rounded disabled:opacity-40">
                Next
              </button>
            </div>
          </div>
        </div>
      )}

      {creating && <DeviceFormModal fixedTenantId={effectiveTenantId} onClose={() => setCreating(false)} onSaved={query.refetch} />}
      {editing && <DeviceFormModal device={editing} onClose={() => setEditing(null)} onSaved={query.refetch} />}
      {deleting && (
        <ConfirmModal
          title="Delete device"
          message={`Delete "${deleting.name}"? This cannot be undone.`}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await DevicesApi.remove(deleting.id);
            query.refetch();
          }}
        />
      )}
    </div>
  );
}
