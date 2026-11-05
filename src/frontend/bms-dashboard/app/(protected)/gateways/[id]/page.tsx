"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { GatewaysApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { StatusBadge } from "@/components/ui/States";
import { Loader2 } from "lucide-react";

type GatewayMessage = { gatewayCode: string; topic: string; receivedAt: string; payload: string };
type ParsedResponse = { message: GatewayMessage; data: Record<string, unknown> | null };

function parsePayload(payload: string): Record<string, unknown> | null {
  try {
    const value = JSON.parse(payload) as unknown;
    return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
  } catch { return null; }
}

function flatten(value: unknown, prefix = ""): Array<[string, string]> {
  if (value == null) return [];
  if (typeof value !== "object" || Array.isArray(value)) return [[prefix, String(value)]];
  return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) => flatten(child, prefix ? `${prefix}.${key}` : key));
}

function responseType(response: ParsedResponse) {
  const type = response.data?.type;
  if (typeof type === "string") return type;
  return response.message.topic.split("/").pop() || "response";
}

export default function GatewayDetailPage() {
  const params = useParams<{ id: string }>();
  const gateway = useApi(() => GatewaysApi.get(params.id), [params.id]);
  const gatewayList = useApi(() => GatewaysApi.list({ page: 1, pageSize: 500 }), [params.id]);
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [messages, setMessages] = useState<GatewayMessage[]>([]);
  const [responses, setResponses] = useState<ParsedResponse[]>([]);

  useEffect(() => { setBusy(null); setNotice(null); setMessages([]); setResponses([]); }, [params.id]);

  useRealtime({ GatewayMessageReceived: (value) => {
    const message = value as GatewayMessage;
    if (!message || message.gatewayCode !== (gateway.data?.gatewayCode ?? "")) return;
    const data = parsePayload(message.payload);
    setMessages(previous => [message, ...previous].slice(0, 30));
    setResponses(previous => [{ message, data }, ...previous].slice(0, 30));
    setNotice(`Response received on ${message.topic}.`);
    setBusy(null);
  }});

  async function send(op: string) {
    setBusy(op); setNotice(null);
    try {
      await GatewaysApi.command(params.id, op);
      setNotice(`${op.replaceAll("_", " ")} request sent - waiting for gateway response...`);
      window.setTimeout(() => setBusy(current => current === op ? null : current), 15000);
    } catch (e) { setBusy(null); setNotice(e instanceof Error ? e.message : "Command failed"); }
  }

  if (gateway.loading) return <main className="p-8 text-sm text-muted-foreground">Loading gateway...</main>;
  const listItem = gatewayList.data?.items.find(x => x.id === gateway.data?.id);
  const g = gateway.data ? { ...gateway.data, tenantName: gateway.data.tenantName || listItem?.tenantName, siteName: gateway.data.siteName || listItem?.siteName } : undefined;
  if (gateway.error || !g) return <main className="p-8"><Link href="/gateways" className="text-primary hover:underline">&larr; Back to gateways</Link><p className="mt-6 text-destructive">{gateway.error || "Gateway not found"}</p></main>;

  const latestStatus = responses.find(r => r.data?.type === "status")?.data;
  const health = latestStatus ? {
    firmware: (latestStatus.firmware as Record<string, unknown> | undefined)?.stm32,
    sensor: (latestStatus.sensor as Record<string, unknown> | undefined)?.name,
    connected: (latestStatus.sensor as Record<string, unknown> | undefined)?.connected,
    ip: (latestStatus.network as Record<string, unknown> | undefined)?.ip,
    rssi: (latestStatus.network as Record<string, unknown> | undefined)?.rssi_dbm,
    uptime: latestStatus.uptime_s,
    gps: (latestStatus.gnss as Record<string, unknown> | undefined)?.fix,
  } : null;
  const commands = [["get_status", "Status"], ["get_latest", "Latest reading"], ["get_config", "Configuration"], ["get_capabilities", "Capabilities"], ["get_history", "History"], ["get_logs", "Logs"], ["recover_cellular", "Recover cellular"]] as const;
  const summaryCards: Array<[string, string]> = [["Tenant", g.tenantName || "- Unassigned -"], ["Site", g.siteName || "- Unassigned -"], ["Last seen", g.lastSeenAt ? new Date(g.lastSeenAt).toLocaleString() : "Never"], ["Last measurement", g.lastMeasurementAt ? new Date(g.lastMeasurementAt).toLocaleString() : "-"], ["IP address", String(g.ipAddress || health?.ip || "-")], ["Firmware", String(g.firmwareVersion || health?.firmware || "-")], ["Signal", g.rssi == null ? (health?.rssi != null ? `${health.rssi} dBm` : "-") : `${g.rssi} dBm`], ["Battery", g.batteryPct == null ? "-" : `${g.batteryPct}%`]];
  const healthCards: Array<[string, string]> = health ? [["Sensor", String(health.sensor ?? "-")], ["Uptime", health.uptime == null ? "-" : `${health.uptime} s`], ["Network", String(health.ip ?? "-")], ["GNSS", health.gps == null ? "-" : String(health.gps)]] : [];

  return <main className="space-y-6 p-6 lg:p-8">
    <div><Link href="/gateways" className="text-sm text-muted-foreground hover:underline">&larr; Gateways</Link><div className="mt-3 flex flex-wrap items-center justify-between gap-3"><div><h1 className="text-2xl font-semibold">{g.name || g.gatewayCode}</h1><p className="font-mono text-sm text-muted-foreground">{g.gatewayCode}</p></div><StatusBadge status={g.status === "Standalone" ? "Unassigned" : g.status} /></div></div>
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{summaryCards.map(([label, value]) => <div key={label} className="rounded-lg border border-border bg-card p-4"><div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div><div className="mt-2 font-medium">{value}</div></div>)}</section>
    {health && <section className="rounded-lg border border-border bg-card p-5"><div className="flex items-center justify-between"><h2 className="font-semibold">Latest gateway status</h2><span className={health.connected === true ? "text-emerald-600" : "text-muted-foreground"}>{health.connected === true ? "Connected" : "Reported"}</span></div><div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">{healthCards.map(([label, value]) => <div key={label} className="rounded-md bg-muted/50 p-3"><div className="text-xs text-muted-foreground">{label}</div><div className="mt-1 text-sm font-medium">{value}</div></div>)}</div></section>}
    <section className="grid gap-4 lg:grid-cols-[1.2fr_1fr]"><div className="rounded-lg border border-border bg-card p-5"><div className="flex items-center justify-between"><h2 className="font-semibold">Gateway monitoring</h2><span className="text-xs text-muted-foreground">Requests are sent to the gateway</span></div><div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4">{commands.map(([op, label]) => <button key={op} onClick={() => send(op)} disabled={busy !== null} className="rounded-md border border-border px-3 py-2 text-left text-sm hover:bg-muted disabled:opacity-50">{busy === op ? <span className="inline-flex items-center gap-2"><Loader2 aria-hidden="true" className="h-3.5 w-3.5 animate-spin" />Waiting...</span> : label}</button>)}</div>{notice && <p role="status" className="mt-3 rounded-md bg-muted px-3 py-2 text-sm">{notice}</p>}<p className="mt-4 text-xs text-muted-foreground">Each command is matched to its ACK or response packet below. The page updates in place; no full refresh is needed.</p></div><div className="rounded-lg border border-border bg-card p-5"><h2 className="font-semibold">Mapped devices</h2>{g.deviceNames?.length ? <ul className="mt-3 list-disc pl-5 text-sm">{g.deviceNames.map(d => <li key={d}>{d}</li>)}</ul> : <p className="mt-3 text-sm text-muted-foreground">No devices mapped. This gateway is standalone.</p>}<div className="mt-5 border-t border-border pt-4"><h3 className="text-sm font-medium">Command channel</h3><code className="mt-2 block break-all rounded bg-muted px-2 py-2 text-xs">protec/gateway/{g.gatewayCode}/command</code></div></div></section>
    <section className="rounded-lg border border-border bg-card p-5"><div className="flex items-center justify-between"><h2 className="font-semibold">Command responses</h2><span className="text-xs text-muted-foreground">Structured MQTT responses</span></div>{responses.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No response received in this session yet.</p> : <div className="mt-3 grid gap-3 md:grid-cols-2">{responses.map((response, index) => <article key={`${response.message.receivedAt}-${index}`} className="rounded-md border border-border bg-muted/20 p-3"><div className="flex justify-between gap-2"><span className="font-medium capitalize">{responseType(response).replaceAll("_", " ")}</span><span className="text-xs text-muted-foreground">{new Date(response.message.receivedAt).toLocaleTimeString()}</span></div><div className="mt-2 text-xs text-muted-foreground">{response.message.topic}</div>{response.data ? <dl className="mt-3 grid gap-x-4 gap-y-1 sm:grid-cols-2">{flatten(response.data).filter(([key]) => key !== "type").slice(0, 16).map(([key, value]) => <div key={key} className="min-w-0"><dt className="truncate text-xs text-muted-foreground">{key}</dt><dd className="truncate text-sm">{value}</dd></div>)}</dl> : <pre className="mt-2 whitespace-pre-wrap break-words text-xs">{response.message.payload}</pre>}</article>)}</div>}</section>
    <details className="rounded-lg border border-border bg-card p-5"><summary className="cursor-pointer font-semibold text-sm">Raw MQTT packets <span className="ml-2 text-xs font-normal text-muted-foreground">Last {messages.length} packets</span></summary>{messages.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No packets received in this session yet.</p> : <div className="mt-3 max-h-96 space-y-3 overflow-auto">{messages.map((message, index) => <div key={`${message.receivedAt}-${index}`} className="rounded-md border border-border bg-muted/30 p-3"><div className="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground"><span className="font-mono">{message.topic}</span><span>{new Date(message.receivedAt).toLocaleString()}</span></div><pre className="mt-2 whitespace-pre-wrap break-words text-xs">{message.payload}</pre></div>)}</div>}</details>
  </main>;
}
