"use client";

import { useState } from "react";
import { GatewaysApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { ApiError } from "@/lib/api/client";
import { useCan } from "@/lib/auth/permissions";

function statusLabel(status: string) {
  if (status === "Online") return "🟢 Online";
  if (status === "Offline") return "🔴 Offline";
  if (status === "Maintenance") return "🟠 Maintenance";
  return "⚪ Unassigned";
}

export default function GatewaysPage() {
  const can = useCan();
  const [refresh, setRefresh] = useState(0);
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const gateways = useApi(() => GatewaysApi.list({ pageSize: 100 }), [refresh]);

  async function register(e: React.FormEvent) {
    e.preventDefault(); setError(null);
    try { await GatewaysApi.create({ gatewayCode: code.trim(), name: name.trim() }); setCode(""); setName(""); setShowForm(false); setRefresh((v) => v + 1); }
    catch (err) { setError(err instanceof ApiError ? err.message : "Unable to register gateway"); }
  }

  return <main className="space-y-6 p-6">
    <div className="flex items-center justify-between"><div><h1 className="text-2xl font-semibold">Gateways</h1><p className="text-sm text-muted-foreground">Standalone gateway inventory and hardware status.</p></div>{can("technical.view") && <button className="rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground" onClick={() => setShowForm(true)}>+ Register Gateway</button>}</div>
    {gateways.error && <p className="text-sm text-destructive">{gateways.error}</p>}
    <div className="overflow-x-auto rounded-lg border border-border"><table className="w-full text-sm"><thead className="bg-muted/40"><tr>{["Status", "Gateway code", "Name", "Tenant", "Site", "Mapped devices", "Last seen"].map((h) => <th key={h} className="px-4 py-3 text-left font-medium">{h}</th>)}</tr></thead><tbody>{(gateways.data?.items ?? []).map((g) => <tr key={g.id} className="border-t border-border"><td className="px-4 py-3">{statusLabel(g.status)}</td><td className="px-4 py-3 font-mono">{g.gatewayCode}</td><td className="px-4 py-3">{g.name}</td><td className="px-4 py-3">{g.tenantId ?? "— Unassigned —"}</td><td className="px-4 py-3">{g.siteName}</td><td className="px-4 py-3">{g.deviceNames?.join(", ") || "0"}</td><td className="px-4 py-3">{g.lastSeenAt ? new Date(g.lastSeenAt).toLocaleString() : "Never"}</td></tr>)}</tbody></table></div>
    {showForm && <div className="fixed inset-0 z-50 grid place-items-center bg-black/40 p-4"><form onSubmit={register} className="w-full max-w-md space-y-4 rounded-lg bg-background p-6 shadow-xl"><h2 className="text-lg font-semibold">Register Gateway</h2><label className="block text-sm">Gateway code<input required value={code} onChange={(e) => setCode(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2" placeholder="860738070588839" /></label><label className="block text-sm">Name<input required value={name} onChange={(e) => setName(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2" placeholder="Edge Gateway 01" /></label>{error && <p className="text-sm text-destructive">{error}</p>}<div className="flex justify-end gap-2"><button type="button" onClick={() => setShowForm(false)} className="rounded-md border border-border px-4 py-2">Cancel</button><button className="rounded-md bg-primary px-4 py-2 text-primary-foreground">Register</button></div></form></div>}
  </main>;
}
