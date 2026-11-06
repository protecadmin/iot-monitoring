"use client";

import { useState } from "react";
import { ScrollText } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { AuditLogApi } from "@/lib/api/resources";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import type { AuditLogDto } from "@/types/api";

function DetailsCell({ json }: { json: string | null }) {
  const [expanded, setExpanded] = useState(false);
  if (!json) return <span className="text-muted-foreground">—</span>;

  let pretty = json;
  try {
    pretty = JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    // Not valid JSON — show the raw string as-is.
  }

  return (
    <button onClick={() => setExpanded((e) => !e)} className="text-left">
      {expanded ? (
        <pre className="text-xs font-mono whitespace-pre-wrap max-w-md">{pretty}</pre>
      ) : (
        <span className="text-xs text-primary hover:underline">View</span>
      )}
    </button>
  );
}

export default function AuditLogPage() {
  const { user } = useAuth();
  const { effectiveTenantId } = useTenantScope();
  const [page, setPage] = useState(1);
  const pageSize = 50;

  const query = useApi(
    () => AuditLogApi.list({ tenantId: effectiveTenantId, page, pageSize }),
    [effectiveTenantId, page],
  );

  if (user?.role !== "SuperAdmin") {
    return <p className="text-sm text-muted-foreground">Only SuperAdmin accounts can view the audit log.</p>;
  }

  const totalPages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / pageSize)) : 1;

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2">
        <ScrollText className="h-5 w-5 text-muted-foreground mt-0.5" />
        <div>
          <h1 className="text-xl font-semibold text-foreground">Audit Log</h1>
          <p className="text-xs text-muted-foreground mt-0.5">Who did what, when — every tenant/user/device/MQTT-credential change recorded here.</p>
        </div>
      </div>

      {query.loading && <LoadingState />}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!query.data || query.data.items.length === 0) && (
        <EmptyState message="No audit entries yet" hint="Admin actions will start showing up here as they happen." icon={ScrollText} />
      )}

      {!query.loading && !query.error && query.data && query.data.items.length > 0 && (
        <>
          <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
                <tr>
                  <th className="text-left px-4 py-2">Time</th>
                  <th className="text-left px-4 py-2">User</th>
                  {user?.role === "SuperAdmin" && <th className="text-left px-4 py-2">Tenant</th>}
                  <th className="text-left px-4 py-2">Action</th>
                  <th className="text-left px-4 py-2">Entity</th>
                  <th className="text-left px-4 py-2">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {query.data.items.map((a: AuditLogDto) => (
                  <tr key={a.id}>
                    <td className="px-4 py-2 text-muted-foreground text-xs whitespace-nowrap">{new Date(a.createdAt).toLocaleString()}</td>
                    <td className="px-4 py-2 text-foreground">{a.userEmail ?? "—"}</td>
                    {user?.role === "SuperAdmin" && <td className="px-4 py-2 text-muted-foreground">{a.tenantName ?? "—"}</td>}
                    <td className="px-4 py-2 font-mono text-xs text-foreground">{a.action}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">
                      {a.entityType ?? "—"}
                      {a.entityId && <span className="text-muted-foreground/70"> ({a.entityId.slice(0, 8)})</span>}
                    </td>
                    <td className="px-4 py-2">
                      <DetailsCell json={a.detailsJson} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex items-center justify-between text-xs text-muted-foreground">
            <span>
              Page {page} of {totalPages} ({query.data.totalCount} entries)
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
        </>
      )}
    </div>
  );
}
