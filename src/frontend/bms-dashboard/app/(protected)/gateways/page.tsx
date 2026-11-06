"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import {
  Activity,
  Cpu,
  Plus,
  Radio,
  RefreshCw,
  Search,
  Server,
  Signal,
  Wifi,
} from "lucide-react";
import { GatewaysApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { ApiError } from "@/lib/api/client";
import { useCan } from "@/lib/auth/permissions";

const badge = (status: string) =>
  status === "Online"
    ? "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
    : status === "Offline"
      ? "bg-red-500/10 text-red-600 border-red-500/20"
      : status === "Maintenance"
        ? "bg-amber-500/10 text-amber-600 border-amber-500/20"
        : "bg-slate-500/10 text-slate-600 border-slate-500/20";

export default function GatewaysPage() {
  const can = useCan();
  const [refresh, setRefresh] = useState(0),
    [showForm, setShowForm] = useState(false),
    [code, setCode] = useState(""),
    [search, setSearch] = useState(""),
    [status, setStatus] = useState(""),
    [error, setError] = useState<string | null>(null);
  const gateways = useApi(
    () =>
      GatewaysApi.list({
        pageSize: 100,
        search: search || undefined,
        status: status || undefined,
      }),
    [refresh, search, status],
  );
  const items = useMemo(() => gateways.data?.items ?? [], [gateways.data?.items]);
  const counts = useMemo(
    () => ({
      total: items.length,
      online: items.filter((g) => g.status === "Online").length,
      offline: items.filter((g) => g.status === "Offline").length,
      unassigned: items.filter((g) => g.status === "Standalone").length,
    }),
    [items],
  );
  async function register(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await GatewaysApi.create({ gatewayCode: code.trim() });
      setCode("");
      setShowForm(false);
      setRefresh((v) => v + 1);
    } catch (err) {
      setError(
        err instanceof ApiError ? err.message : "Unable to register gateway",
      );
    }
  }

  return (
    <main className="space-y-6 p-6 lg:p-8">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <Radio className="h-6 w-6 text-primary" />
            <h1 className="text-2xl font-bold tracking-tight">Gateway Fleet</h1>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Monitor gateway connectivity, hardware health, and connected
            devices.
          </p>
        </div>
        {can("technical.view") && (
          <button
            onClick={() => setShowForm(true)}
            className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground shadow-sm hover:bg-primary/90"
          >
            <Plus className="h-4 w-4" />
            Register Gateway
          </button>
        )}
      </div>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {([
          ["Total Gateways", counts.total, Server, "text-foreground"],
          ["Online", counts.online, Activity, "text-emerald-600"],
          ["Offline", counts.offline, Activity, "text-red-500"],
          ["Unassigned", counts.unassigned, Cpu, "text-blue-500"],
        ] as const).map(([label, value, Icon, color]) => (
          <div
            key={String(label)}
            className="rounded-xl border border-border bg-card p-4 shadow-sm"
          >
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                {label}
              </span>
              <Icon className={`h-4 w-4 ${color}`} />
            </div>
            <div className={`mt-2 text-2xl font-bold ${color}`}>{value}</div>
          </div>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-[220px] max-w-md flex-1">
          <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by gateway code or name..."
            className="w-full rounded-lg border border-border bg-background py-2 pl-9 pr-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
          />
        </div>
        <select
          aria-label="Gateway status"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className="rounded-lg border border-border bg-background px-3 py-2 text-sm"
        >
          <option value="">All Statuses</option>
          <option value="Online">Online</option>
          <option value="Standalone">Unassigned</option>
          <option value="Offline">Offline</option>
          <option value="Maintenance">Maintenance</option>
        </select>
        <button
          onClick={() => setRefresh((v) => v + 1)}
          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-card px-3 py-2 text-sm font-medium hover:bg-muted"
        >
          <RefreshCw className="h-4 w-4" />
          Refresh
        </button>
      </div>
      {gateways.error && (
        <div className="rounded-lg border border-destructive/20 bg-destructive/10 p-4 text-sm text-destructive">
          Unable to load gateways. Please try again.
        </div>
      )}
      <div className="overflow-hidden rounded-xl border border-border bg-card shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[980px] text-left text-sm">
            <thead className="border-b border-border bg-muted/40 text-xs font-semibold uppercase text-muted-foreground">
              <tr>
                {[
                  "Status",
                  "Gateway ID",
                  "Name",
                  "Tenant & Site",
                  "Firmware",
                  "Signal",
                  "Connected Devices",
                  "Last Seen",
                  "Actions",
                ].map((h) => (
                  <th key={h} className="px-5 py-3.5">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {items.map((g) => (
                <tr key={g.id} className="transition-colors hover:bg-muted/30">
                  <td className="whitespace-nowrap px-5 py-3.5">
                    <span
                      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium ${badge(g.status)}`}
                    >
                      <span
                        className={`h-1.5 w-1.5 rounded-full ${g.status === "Online" ? "animate-pulse bg-emerald-500" : g.status === "Offline" ? "bg-red-500" : "bg-slate-400"}`}
                      />
                      {g.status === "Standalone" ? "Unassigned" : g.status}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5 font-mono font-semibold text-primary">
                    {g.gatewayCode}
                  </td>
                  <td className="px-5 py-3.5 font-medium">
                    {g.name || g.gatewayCode}
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5 text-muted-foreground">
                    <div>{g.siteName || "— Unassigned —"}</div>
                    {g.tenantName && (
                      <div className="text-xs text-muted-foreground/75">
                        {g.tenantName}
                      </div>
                    )}
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5">
                    {g.firmwareVersion || "—"}
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5">
                    <span className="inline-flex items-center gap-1.5 text-xs">
                      <Signal className="h-3.5 w-3.5 text-primary" />
                      {g.rssi != null ? `${g.rssi} dBm` : "—"}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5">
                    <span className="inline-flex items-center gap-1.5">
                      <Wifi className="h-3.5 w-3.5 text-emerald-600" />
                      {g.deviceNames?.length ?? 0}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5 text-xs text-muted-foreground">
                    {g.lastSeenAt
                      ? new Date(g.lastSeenAt).toLocaleString()
                      : "Never"}
                  </td>
                  <td className="whitespace-nowrap px-5 py-3.5 text-right">
                    <Link
                      href={`/gateways/${g.id}`}
                      className="inline-flex items-center rounded-md bg-secondary px-3 py-1.5 text-xs font-medium text-secondary-foreground hover:bg-secondary/80"
                    >
                      Inspect &amp; Monitor
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {gateways.loading && (
          <div className="p-12 text-center text-sm text-muted-foreground">
            Loading gateways...
          </div>
        )}
        {!gateways.loading && !gateways.error && items.length === 0 && (
          <div className="p-12 text-center">
            <Radio className="mx-auto h-10 w-10 text-muted-foreground/50" />
            <h3 className="mt-3 text-sm font-semibold">No Gateways Found</h3>
            <p className="mx-auto mt-1 max-w-sm text-xs text-muted-foreground">
              Register a gateway or publish its first MQTT packet to
              auto-discover it.
            </p>
          </div>
        )}
      </div>
      {showForm && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4 backdrop-blur-sm">
          <form
            onSubmit={register}
            className="w-full max-w-md space-y-4 rounded-xl border border-border bg-card p-6 shadow-2xl"
          >
            <div className="flex items-center gap-2">
              <Radio className="h-5 w-5 text-primary" />
              <h2 className="text-lg font-bold">Register Gateway</h2>
            </div>
            <p className="text-xs text-muted-foreground">
              Only the hardware gateway code is required.
            </p>
            <label className="block text-sm">
              <span className="font-medium">Gateway Code *</span>
              <input
                required
                value={code}
                onChange={(e) => setCode(e.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm"
                placeholder="860738070588839"
              />
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
                Register Gateway
              </button>
            </div>
          </form>
        </div>
      )}
    </main>
  );
}
