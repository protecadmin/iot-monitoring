"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { GatewaysApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";

export default function GatewayDetailPage() {
  const params = useParams<{ id: string }>();
  const gateway = useApi(() => GatewaysApi.get(params.id), [params.id]);
  const g = gateway.data;

  if (gateway.loading) return <main className="p-8 text-sm text-muted-foreground">Loading gateway…</main>;
  if (gateway.error || !g) return <main className="p-8"><Link href="/gateways" className="text-primary hover:underline">← Back to gateways</Link><p className="mt-6 text-destructive">{gateway.error || "Gateway not found"}</p></main>;

  return <main className="space-y-6 p-6 lg:p-8">
    <div><Link href="/gateways" className="text-sm text-muted-foreground hover:underline">← Gateways</Link><div className="mt-3 flex flex-wrap items-center justify-between gap-3"><div><h1 className="text-2xl font-semibold">{g.name || g.gatewayCode}</h1><p className="font-mono text-sm text-muted-foreground">{g.gatewayCode}</p></div><span className="rounded-full border px-3 py-1 text-sm">{g.status === "Standalone" ? "Unassigned" : g.status}</span></div></div>
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{[["Tenant", g.tenantName || "— Unassigned —"],["Site", g.siteName || "— Unassigned —"],["Last seen", g.lastSeenAt ? new Date(g.lastSeenAt).toLocaleString() : "Never"],["Last measurement", g.lastMeasurementAt ? new Date(g.lastMeasurementAt).toLocaleString() : "—"],["IP address", g.ipAddress || "—"],["Firmware", g.firmwareVersion || "—"],["Signal", g.rssi == null ? "—" : `${g.rssi} dBm`],["Battery", g.batteryPct == null ? "—" : `${g.batteryPct}%`]].map(([label,value]) => <div key={label} className="rounded-lg border border-border bg-card p-4"><div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div><div className="mt-2 font-medium">{value}</div></div>)}</section>
    <section className="rounded-lg border border-border bg-card p-5"><h2 className="font-semibold">Mapped devices</h2>{g.deviceNames?.length ? <ul className="mt-3 list-disc pl-5 text-sm">{g.deviceNames.map(d => <li key={d}>{d}</li>)}</ul> : <p className="mt-3 text-sm text-muted-foreground">No devices mapped. This gateway is standalone.</p>}</section>
  </main>;
}
