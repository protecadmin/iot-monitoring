"use client";

import { useState } from "react";
import { Plug, Plus } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { ConnectorsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { useCan } from "@/lib/auth/permissions";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/ui/States";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { ConnectorFormModal } from "@/components/connectors/ConnectorFormModal";
import { DeviceFormModal } from "@/components/devices/DeviceFormModal";
import type { ConnectorDto, ConnectorUnknownDeviceDto } from "@/types/api";

const FORMAT_LABEL: Record<string, string> = { Csv: "CSV", KeyValue: "Key = value", Json: "JSON", Binary: "Binary" };

function ago(iso: string | null) {
  if (!iso) return "never";
  const s = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 90) return `${s} s ago`;
  if (s < 5400) return `${Math.round(s / 60)} min ago`;
  if (s < 172800) return `${Math.round(s / 3600)} h ago`;
  return `${Math.round(s / 86400)} days ago`;
}

/**
 * Connectors: devices that send their own format (raw TCP, CSV, JSON,
 * binary …) are read by the Connector Service and handed to BMS as normal
 * readings. A SuperAdmin sets each vendor up here once; adding a device of
 * that kind is then just picking the connector and typing its sensor ID.
 */
export default function ConnectorsPage() {
  const can = useCan();
  const canManage = can("connectors.manage");
  const { effectiveTenantId } = useTenantScope();
  const listQuery = useApi(() => ConnectorsApi.list(), []);
  const statusQuery = useApi(() => (canManage ? ConnectorsApi.serviceStatus() : Promise.resolve(undefined)), [canManage]);
  const unknownQuery = useApi(() => ConnectorsApi.unknownDevices(), []);

  const [editing, setEditing] = useState<ConnectorDto | null>(null);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<ConnectorDto | null>(null);
  const [adding, setAdding] = useState<ConnectorUnknownDeviceDto | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const status = statusQuery.data;
  const refresh = () => {
    listQuery.refetch();
    unknownQuery.refetch();
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Connectors</h1>
          <p className="text-sm text-muted-foreground">For devices that send their own format over TCP — each is translated into a normal BMS reading.</p>
        </div>
        {canManage && (
          <button onClick={() => setCreating(true)} className="inline-flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90">
            <Plus className="h-4 w-4" /> Add connector
          </button>
        )}
      </div>

      {canManage && (
        <section className="bg-card border border-border rounded-lg p-4 shadow-sm">
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
            <span className="flex items-center gap-2 font-medium">
              <span className={`h-2.5 w-2.5 rounded-full ${status?.online ? "bg-emerald-500" : "bg-red-500"}`} />
              Connector Service {status?.online ? "online" : "offline"}
            </span>
            <span className="text-muted-foreground">Last heartbeat: {ago(status?.lastHeartbeatAt ?? null)}</span>
            {status && (
              <>
                <span className="text-muted-foreground">Accepted {status.messagesAccepted.toLocaleString()}</span>
                <span className="text-muted-foreground">Published {status.messagesPublished.toLocaleString()}</span>
                <span className={status.messagesRejected > 0 ? "text-amber-700 dark:text-amber-400" : "text-muted-foreground"}>
                  Rejected {status.messagesRejected.toLocaleString()}
                </span>
                <span className="text-muted-foreground">Unknown sensor {status.messagesUnknown.toLocaleString()}</span>
                <span className="text-muted-foreground">
                  Ports {status.portRangeStart}–{status.portRangeEnd}
                </span>
              </>
            )}
          </div>
          {status?.lastError && <p className="text-xs text-destructive mt-2">Last error: {status.lastError}</p>}
          {status && !status.online && (
            <p className="text-xs text-muted-foreground mt-2">
              No heartbeat — the Connector Service isn&apos;t running or can&apos;t reach the API. Devices on connectors deliver nothing until it&apos;s back.
            </p>
          )}
        </section>
      )}

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-foreground">Connectors</h2>
        {actionError && <p className="text-xs text-destructive">{actionError}</p>}
        {listQuery.loading ? (
          <TableSkeleton />
        ) : listQuery.error ? (
          <ErrorState message={listQuery.error} />
        ) : !listQuery.data?.length ? (
          <EmptyState icon={Plug} message="No connectors yet" hint="Add one for each kind of device that can't send BMS's MQTT format itself, e.g. a 4G tilt sensor sending CSV over TCP." />
        ) : (
          <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="text-left px-4 py-2">Name</th>
                  <th className="text-left px-4 py-2">Port</th>
                  <th className="text-left px-4 py-2">Format</th>
                  <th className="text-left px-4 py-2">Device code</th>
                  <th className="text-left px-4 py-2">Devices</th>
                  <th className="text-left px-4 py-2">Status</th>
                  {canManage && <th className="text-left px-4 py-2">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {listQuery.data.map((c) => (
                  <tr key={c.id} className={c.isEnabled ? undefined : "opacity-60"}>
                    <td className="px-4 py-2">
                      <div className="font-medium text-foreground">{c.name}</div>
                      {c.vendor && <div className="text-xs text-muted-foreground">{c.vendor}</div>}
                    </td>
                    <td className="px-4 py-2 tabular-nums">TCP {c.port}</td>
                    <td className="px-4 py-2">{FORMAT_LABEL[c.format] ?? c.format}</td>
                    <td className="px-4 py-2 font-mono text-xs">{c.deviceCodePrefix}…</td>
                    <td className="px-4 py-2 tabular-nums">{c.deviceCount}</td>
                    <td className="px-4 py-2 text-xs">
                      {c.isEnabled ? <span className="text-emerald-600 dark:text-emerald-400">Enabled</span> : "Disabled"}
                      {c.unmappedKeyCount > 0 && (
                        <span className="ml-2 text-amber-700 dark:text-amber-400" title="Message parts arriving that no mapping uses">
                          {c.unmappedKeyCount} unmapped
                        </span>
                      )}
                    </td>
                    {canManage && (
                      <td className="px-4 py-2 whitespace-nowrap">
                        <button onClick={() => setEditing(c)} className="text-xs text-primary hover:underline mr-3">
                          Edit
                        </button>
                        <button onClick={() => setDeleting(c)} className="text-xs text-destructive hover:underline">
                          Delete
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-foreground">Unknown devices</h2>
        <p className="text-xs text-muted-foreground">Sensor IDs sending to a connector that match no device — usually installed before being added here, or a typo.</p>
        {!unknownQuery.data?.length ? (
          <p className="text-sm text-muted-foreground">None — every sensor sending in is registered.</p>
        ) : (
          <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="text-left px-4 py-2">Sensor ID</th>
                  <th className="text-left px-4 py-2">Connector</th>
                  <th className="text-left px-4 py-2">Messages</th>
                  <th className="text-left px-4 py-2">Last seen</th>
                  <th className="text-left px-4 py-2">Last message</th>
                  <th className="text-left px-4 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {unknownQuery.data.map((u) => (
                  <tr key={`${u.connectorId}:${u.sensorId}`}>
                    <td className="px-4 py-2 font-mono text-xs">{u.sensorId}</td>
                    <td className="px-4 py-2">{u.connectorName}</td>
                    <td className="px-4 py-2 tabular-nums">{u.messageCount.toLocaleString()}</td>
                    <td className="px-4 py-2 text-xs text-muted-foreground">{ago(u.lastSeenAt)}</td>
                    <td className="px-4 py-2 font-mono text-xs text-muted-foreground max-w-[18rem] truncate" title={u.lastSample ?? ""}>
                      {u.lastSample ?? "—"}
                    </td>
                    <td className="px-4 py-2 whitespace-nowrap">
                      {can("devices.edit") && (
                        <button onClick={() => setAdding(u)} className="text-xs text-primary hover:underline mr-3">
                          Add device
                        </button>
                      )}
                      {canManage && (
                        <button
                          onClick={async () => {
                            setActionError(null);
                            try {
                              await ConnectorsApi.dismissUnknown(u.connectorId, u.sensorId);
                              unknownQuery.refetch();
                            } catch (err) {
                              setActionError(err instanceof ApiError ? err.message : "Couldn't dismiss it");
                            }
                          }}
                          className="text-xs text-muted-foreground hover:text-foreground"
                        >
                          Dismiss
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {(creating || editing) && (
        <ConnectorFormModal
          connector={editing ?? undefined}
          onClose={() => {
            setCreating(false);
            setEditing(null);
          }}
          onSaved={refresh}
        />
      )}

      {deleting && (
        <ConfirmModal
          title={`Delete connector "${deleting.name}"?`}
          message={
            deleting.deviceCount > 0
              ? `${deleting.deviceCount} device(s) still use it — delete or move them first, or disable the connector instead.`
              : "The Connector Service stops listening on its port. This can't be undone."
          }
          confirmLabel="Delete"
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            setActionError(null);
            try {
              await ConnectorsApi.remove(deleting.id);
              refresh();
            } catch (err) {
              setActionError(err instanceof ApiError ? err.message : "Couldn't delete the connector");
            } finally {
              setDeleting(null);
            }
          }}
        />
      )}

      {adding && (
        <DeviceFormModal
          fixedTenantId={effectiveTenantId}
          prefill={{ connectorId: adding.connectorId, sensorId: adding.sensorId }}
          onClose={() => setAdding(null)}
          onSaved={refresh}
        />
      )}
    </div>
  );
}
