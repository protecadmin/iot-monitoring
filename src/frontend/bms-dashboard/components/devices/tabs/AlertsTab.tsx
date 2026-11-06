"use client";

import { useApi } from "@/hooks/useApi";
import { AlertsApi } from "@/lib/api/resources";
import { useCan } from "@/lib/auth/permissions";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";

export function AlertsTab({ deviceId }: { deviceId: string }) {
  const query = useApi(() => AlertsApi.list({ deviceId, pageSize: 100 }), [deviceId]);
  const canHandle = useCan()("alerts.handle");
  const canResolve = useCan()("alerts.resolve");

  if (query.loading) return <LoadingState />;
  if (query.error) return <ErrorState message={query.error} />;
  if (!query.data || query.data.items.length === 0) return <EmptyState message="No alerts for this device." />;

  async function act(id: string, action: "acknowledge" | "resolve") {
    if (action === "acknowledge") await AlertsApi.acknowledge(id);
    else await AlertsApi.resolve(id);
    query.refetch();
  }

  return (
    <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
      <table className="w-full text-sm">
        <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
          <tr>
            <th className="text-left px-4 py-2">Severity</th>
            <th className="text-left px-4 py-2">Parameter</th>
            <th className="text-left px-4 py-2">Value / Threshold</th>
            <th className="text-left px-4 py-2">Triggered</th>
            <th className="text-left px-4 py-2">Status</th>
            {canHandle && <th className="text-left px-4 py-2">Actions</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {query.data.items.map((a) => (
            <tr key={a.id}>
              <td className="px-4 py-2">
                <StatusBadge status={a.severity} />
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
                    <button onClick={() => act(a.id, "resolve")} className="text-xs text-emerald-600 hover:underline">
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
  );
}
