"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { HubsApi, SitesApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { ApiError } from "@/lib/api/client";
import { useCan } from "@/lib/auth/permissions";
import { Server, Activity, ShieldCheck, RefreshCw, Plus, Search, Tv } from "lucide-react";

const statusBadge = (s: string) => {
  switch (s) {
    case "Online":
      return "bg-emerald-500/10 text-emerald-600 border-emerald-500/20";
    case "Offline":
      return "bg-red-500/10 text-red-600 border-red-500/20";
    case "Maintenance":
      return "bg-amber-500/10 text-amber-600 border-amber-500/20";
    default:
      return "bg-slate-500/10 text-slate-600 border-slate-500/20";
  }
};

export default function HubsPage() {
  const can = useCan();
  const [refresh, setRefresh] = useState(0);
  const [showForm, setShowForm] = useState(false);
  const [hubId, setHubId] = useState("");
  const [name, setName] = useState("");
  const [siteId, setSiteId] = useState("");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [error, setError] = useState<string | null>(null);

  const hubs = useApi(
    () => HubsApi.list({ pageSize: 100, search: search || undefined, status: status || undefined }),
    [refresh, search, status]
  );
  const sites = useApi(() => SitesApi.list(), []);

  const items = hubs.data?.items ?? [];
  const counts = useMemo(
    () => ({
      total: items.length,
      online: items.filter((h) => h.status === "Online").length,
      offline: items.filter((h) => h.status === "Offline").length,
      standalone: items.filter((h) => h.status === "Standalone").length,
    }),
    [items]
  );

  async function register(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await HubsApi.create({
        hubId: hubId.trim().toLowerCase(),
        name: name.trim() || undefined,
        siteId: siteId || undefined,
        securityMode: "tls",
      });
      setHubId("");
      setName("");
      setSiteId("");
      setShowForm(false);
      setRefresh((v) => v + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Unable to register HUB");
    }
  }

  return (
    <main className="space-y-6 p-6 lg:p-8">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <Server className="h-6 w-6 text-primary" />
            <h1 className="text-2xl font-bold tracking-tight">PRO-TEC HUBs</h1>
          </div>
          <p className="text-sm text-muted-foreground mt-1">
            Fleet monitoring, local HDMI/TV display kiosk configuration, and remote MQTT command dispatch.
          </p>
        </div>
        {can("technical.view") && (
          <button
            onClick={() => setShowForm(true)}
            className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground shadow-sm hover:bg-primary/90 transition-colors"
          >
            <Plus className="h-4 w-4" />
            Register HUB
          </button>
        )}
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {[
          { label: "Total HUBs", value: counts.total, icon: Server, color: "text-foreground" },
          { label: "Online", value: counts.online, icon: Activity, color: "text-emerald-600" },
          { label: "Offline", value: counts.offline, icon: Activity, color: "text-red-500" },
          { label: "Standalone", value: counts.standalone, icon: Tv, color: "text-blue-500" },
        ].map((stat) => (
          <div key={stat.label} className="rounded-xl border border-border bg-card p-4 shadow-sm">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                {stat.label}
              </span>
              <stat.icon className={`h-4 w-4 ${stat.color}`} />
            </div>
            <div className={`mt-2 text-2xl font-bold ${stat.color}`}>{stat.value}</div>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative flex-1 min-w-[220px] max-w-md">
          <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by HUB ID (e.g. 41bfdcac4333) or name..."
            className="w-full rounded-lg border border-border bg-background pl-9 pr-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
          />
        </div>
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className="rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
        >
          <option value="">All Statuses</option>
          <option value="Online">Online</option>
          <option value="Standalone">Standalone</option>
          <option value="Offline">Offline</option>
          <option value="Maintenance">Maintenance</option>
        </select>
        <button
          onClick={() => setRefresh((v) => v + 1)}
          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-card px-3 py-2 text-sm font-medium hover:bg-muted transition-colors"
        >
          <RefreshCw className="h-4 w-4" />
          Refresh
        </button>
      </div>

      {hubs.error && (
        <div className="rounded-lg border border-destructive/20 bg-destructive/10 p-4 text-sm text-destructive">
          {hubs.error}
        </div>
      )}

      {/* Fleet Table */}
      <div className="overflow-hidden rounded-xl border border-border bg-card shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-muted/40 text-xs font-semibold uppercase text-muted-foreground">
              <tr>
                <th className="px-5 py-3.5">Status</th>
                <th className="px-5 py-3.5">HUB ID</th>
                <th className="px-5 py-3.5">Name</th>
                <th className="px-5 py-3.5">Tenant & Site</th>
                <th className="px-5 py-3.5">Security</th>
                <th className="px-5 py-3.5">Display Mode</th>
                <th className="px-5 py-3.5">Config Rev</th>
                <th className="px-5 py-3.5">Last Seen</th>
                <th className="px-5 py-3.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {items.map((hub) => (
                <tr key={hub.id} className="hover:bg-muted/30 transition-colors">
                  <td className="px-5 py-3.5 whitespace-nowrap">
                    <span
                      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium ${statusBadge(
                        hub.status
                      )}`}
                    >
                      <span
                        className={`h-1.5 w-1.5 rounded-full ${
                          hub.status === "Online"
                            ? "bg-emerald-500 animate-pulse"
                            : hub.status === "Offline"
                            ? "bg-red-500"
                            : "bg-slate-400"
                        }`}
                      />
                      {hub.status}
                    </span>
                  </td>
                  <td className="px-5 py-3.5 whitespace-nowrap font-mono font-semibold text-primary">
                    {hub.hubId}
                  </td>
                  <td className="px-5 py-3.5 font-medium">{hub.name}</td>
                  <td className="px-5 py-3.5 whitespace-nowrap text-muted-foreground">
                    <div>{hub.siteName || "— Unassigned —"}</div>
                    {hub.tenantName && (
                      <div className="text-xs text-muted-foreground/75">{hub.tenantName}</div>
                    )}
                  </td>
                  <td className="px-5 py-3.5 whitespace-nowrap">
                    <span className="inline-flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-xs font-mono font-medium">
                      <ShieldCheck className="h-3 w-3 text-emerald-500" />
                      {hub.securityMode || "tls"}
                    </span>
                  </td>
                  <td className="px-5 py-3.5 whitespace-nowrap">
                    <span className="capitalize">{hub.displayMode || "overview"}</span>
                    {hub.rotationSeconds && (
                      <span className="ml-1 text-xs text-muted-foreground">
                        ({hub.rotationSeconds}s)
                      </span>
                    )}
                  </td>
                  <td className="px-5 py-3.5 whitespace-nowrap font-mono text-xs">
                    rev {hub.configRevision}
                  </td>
                  <td className="px-5 py-3.5 whitespace-nowrap text-xs text-muted-foreground">
                    {hub.lastSeenAt ? new Date(hub.lastSeenAt).toLocaleString() : "Never"}
                  </td>
                  <td className="px-5 py-3.5 text-right whitespace-nowrap">
                    <Link
                      href={`/hubs/${hub.id}`}
                      className="inline-flex items-center rounded-md bg-secondary px-3 py-1.5 text-xs font-medium text-secondary-foreground hover:bg-secondary/80 transition-colors"
                    >
                      Inspect & Control
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {!hubs.loading && items.length === 0 && (
          <div className="p-12 text-center">
            <Server className="mx-auto h-10 w-10 text-muted-foreground/50" />
            <h3 className="mt-3 text-sm font-semibold">No HUBs Found</h3>
            <p className="mt-1 text-xs text-muted-foreground max-w-sm mx-auto">
              Register a PRO-TEC HUB using its 12-character hex ID (e.g. 41bfdcac4333) or let the HUB auto-discover on MQTT publish.
            </p>
          </div>
        )}
      </div>

      {/* Registration Modal */}
      {showForm && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 backdrop-blur-sm p-4">
          <form
            onSubmit={register}
            className="w-full max-w-md space-y-4 rounded-xl border border-border bg-card p-6 shadow-2xl"
          >
            <div className="flex items-center gap-2">
              <Server className="h-5 w-5 text-primary" />
              <h2 className="text-lg font-bold">Register PRO-TEC HUB</h2>
            </div>
            <p className="text-xs text-muted-foreground">
              Provide the 12-character identifier found on the HUB label or system status.
            </p>

            <label className="block text-sm">
              <span className="font-medium">HUB Identifier (12 Hex characters) *</span>
              <input
                required
                value={hubId}
                onChange={(e) => setHubId(e.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
                placeholder="41bfdcac4333"
              />
            </label>

            <label className="block text-sm">
              <span className="font-medium">Display Name</span>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
                placeholder="Singapore Soilbuild Hub 1"
              />
            </label>

            <label className="block text-sm">
              <span className="font-medium">Assign Site (Optional)</span>
              <select
                value={siteId}
                onChange={(e) => setSiteId(e.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
              >
                <option value="">— Unassigned (Standalone) —</option>
                {sites.data?.items.map((site) => (
                  <option key={site.id} value={site.id}>
                    {site.name} ({site.code})
                  </option>
                ))}
              </select>
            </label>

            {error && <p className="text-xs text-destructive">{error}</p>}

            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setShowForm(false)}
                className="rounded-lg border border-border px-4 py-2 text-sm font-medium hover:bg-muted"
              >
                Cancel
              </button>
              <button
                type="submit"
                className="rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90"
              >
                Register HUB
              </button>
            </div>
          </form>
        </div>
      )}
    </main>
  );
}
