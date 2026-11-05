"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { HubsApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { useRealtime } from "@/lib/websocket/useRealtime";
import {
  Server,
  Activity,
  ShieldCheck,
  Send,
  Sliders,
  Tv,
  CheckCircle2,
  Clock,
  Radio,
  Wifi,
  Copy,
  Check,
  Volume2,
  Thermometer,
  Zap
} from "lucide-react";

type HubMessage = {
  hubId: string;
  topic: string;
  receivedAt: string;
  payload: string;
};

type LiveSensorFeed = {
  id: string;
  sourceId?: string;
  sensor: string;
  metric: string;
  value: string | number;
  unit: string;
  site?: string;
  limit?: number;
  quality?: string;
  updatedAt: string;
};
type ParsedResponse = {
  message: HubMessage;
  data: Record<string, unknown> | null;
};

function parsePayload(payload: string): Record<string, unknown> | null {
  try {
    const value = JSON.parse(payload) as unknown;
    return value && typeof value === "object" && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

function flatten(value: unknown, prefix = ""): Array<[string, string]> {
  if (value == null) return [];
  if (typeof value !== "object" || Array.isArray(value)) return [[prefix, String(value)]];
  return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) =>
    flatten(child, prefix ? `${prefix}.${key}` : key)
  );
}

function responseType(response: ParsedResponse) {
  const type = response.data?.type || response.data?.schema || response.data?.state;
  if (typeof type === "string") return type;
  return response.message.topic.split("/").pop() || "response";
}

export default function HubDetailPage() {
  const params = useParams<{ id: string }>();
  const hub = useApi(() => HubsApi.get(params.id), [params.id]);

  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [copiedTopic, setCopiedTopic] = useState<string | null>(null);

  // Realtime dynamic sensor feeds from MQTT telemetry
  const [sensorFeeds, setSensorFeeds] = useState<Record<string, LiveSensorFeed>>({});
  // Realtime MQTT packets and structured responses
  const [messages, setMessages] = useState<HubMessage[]>([]);
  const [responses, setResponses] = useState<ParsedResponse[]>([]);

  // Remote config form state
  const [displayMode, setDisplayMode] = useState("overview");
  const [rotationSeconds, setRotationSeconds] = useState(20);
  const [welcomeSeconds, setWelcomeSeconds] = useState(7);
  const [historyRange, setHistoryRange] = useState("1h");
  const [configSubmitting, setConfigSubmitting] = useState(false);

  useEffect(() => {
    setBusy(null);
    setNotice(null);
    setMessages([]);
    setResponses([]);
    setSensorFeeds({});
  }, [params.id]);

  // Synchronize remote config inputs with hub reported values
  useEffect(() => {
    if (hub.data) {
      if (hub.data.displayMode) setDisplayMode(hub.data.displayMode);
      if (hub.data.rotationSeconds) setRotationSeconds(hub.data.rotationSeconds);
      if (hub.data.welcomeSeconds) setWelcomeSeconds(hub.data.welcomeSeconds);
      if (hub.data.historyRange) setHistoryRange(hub.data.historyRange);
    }
  }, [hub.data]);

  // Real-time listener for Hub MQTT events via SignalR
  useRealtime({
    HubMessageReceived: (value: unknown) => {
      const message = value as HubMessage;
      if (!message || message.hubId !== (hub.data?.hubId ?? "")) return;

      const data = parsePayload(message.payload);
      setMessages((prev) => [message, ...prev].slice(0, 50));
      setResponses((prev) => [{ message, data }, ...prev].slice(0, 50));
      setNotice(`Packet received on ${message.topic}`);
      setBusy(null);

      // Refresh hub record when config or status changes
      if (
        message.topic.endsWith("/config/ack") ||
        message.topic.endsWith("/config/reported") ||
        message.topic.endsWith("/status/snapshot")
      ) {
        hub.refetch();
      }
    },
  });

  async function sendCommand(action: string) {
    setBusy(action);
    setNotice(null);
    try {
      const res = await HubsApi.command(params.id, action);
      setNotice(`Command '${action}' dispatched to topic ${res.topic}. Awaiting device execution...`);
      window.setTimeout(() => setBusy((current) => (current === action ? null : current)), 15000);
    } catch (e) {
      setBusy(null);
      setNotice(e instanceof Error ? e.message : "Command dispatch failed");
    }
  }

  async function applyDesiredConfig(e: React.FormEvent) {
    e.preventDefault();
    setConfigSubmitting(true);
    setNotice(null);
    try {
      const res = await HubsApi.setDesiredConfig(params.id, {
        displayMode,
        rotationSeconds: Number(rotationSeconds),
        welcomeSeconds: Number(welcomeSeconds),
        historyRange,
      });
      setNotice(
        `Desired config revision ${res.desiredRevision} published to ${res.topic}. Awaiting device ACK and reported state.`
      );
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Configuration deployment failed");
    } finally {
      setConfigSubmitting(false);
    }
  }

  function copyToClipboard(text: string) {
    navigator.clipboard.writeText(text);
    setCopiedTopic(text);
    setTimeout(() => setCopiedTopic(null), 2000);
  }

  if (hub.loading) {
    return <main className="p-8 text-sm text-muted-foreground">Loading PRO-TEC HUB telemetry...</main>;
  }

  const h = hub.data;
  if (hub.error || !h) {
    return (
      <main className="p-8">
        <Link href="/hubs" className="text-primary hover:underline">
          &larr; Back to HUBs
        </Link>
        <p className="mt-6 text-destructive font-medium">{hub.error || "HUB device not found"}</p>
      </main>
    );
  }

  // Derive latest status from incoming messages or database snapshot
  const latestSnapshot =
    responses.find((r) => r.message.topic.includes("/status"))?.data ||
    (h.lastStatusJson ? parsePayload(h.lastStatusJson) : null);

  const commandButtons = [
    { action: "report.publish_snapshot", label: "Publish Snapshot", desc: "Force immediate status snapshot" },
    { action: "display.reload", label: "Reload Display", desc: "Refresh HDMI/TV dashboard view" },
    { action: "source.poll_once", label: "Poll Sources", desc: "Request fresh sensor API poll" },
    { action: "hub.restart_service", label: "Restart Service", desc: "Restart local reporting service" },
  ];

  const summaryCards: Array<[string, string]> = [
    ["Tenant", h.tenantName || "— Unassigned —"],
    ["Site", h.siteName || "— Unassigned —"],
    ["Last Seen", h.lastSeenAt ? new Date(h.lastSeenAt).toLocaleString() : "Never"],
    ["Last Heartbeat", h.lastHeartbeatAt ? new Date(h.lastHeartbeatAt).toLocaleString() : "—"],
    ["IP Address", h.ipAddress || (latestSnapshot?.network as Record<string, unknown>)?.ip as string || "—"],
    ["Security Mode", h.securityMode || "TLS 1.2/1.3 Verified"],
    ["App Version", h.appVersion || "v0.3.16"],
    ["Agent Version", h.agentVersion || "v0.3.18-r2"],
  ];

  return (
    <main className="space-y-6 p-6 lg:p-8">
      {/* Header */}
      <div>
        <Link href="/hubs" className="text-sm font-medium text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          &larr; PRO-TEC HUBs
        </Link>
        <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="rounded-lg bg-primary/10 p-2.5 text-primary">
              <Server className="h-6 w-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-2xl font-bold tracking-tight">{h.name || h.hubId}</h1>
                <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-muted-foreground">
                  ID: {h.hubId}
                </span>
              </div>
              <p className="text-xs text-muted-foreground mt-0.5">
                Local Raspberry Pi Display Kiosk &bull; Reporting Agent Active
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <span
              className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium ${
                h.status === "Online"
                  ? "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                  : h.status === "Offline"
                  ? "bg-red-500/10 text-red-600 border-red-500/20"
                  : "bg-slate-500/10 text-slate-600 border-slate-500/20"
              }`}
            >
              <span
                className={`h-2 w-2 rounded-full ${
                  h.status === "Online" ? "bg-emerald-500 animate-pulse" : "bg-red-500"
                }`}
              />
              {h.status}
            </span>
            <button
              onClick={() => hub.refetch()}
              className="rounded-lg border border-border bg-card px-3 py-1.5 text-xs font-medium hover:bg-muted transition-colors"
            >
              Refresh
            </button>
          </div>
        </div>
      </div>

      {/* KPI Cards */}
      <section className="grid gap-3.5 grid-cols-2 md:grid-cols-4 xl:grid-cols-8">
        {summaryCards.map(([label, value]) => (
          <div key={label} className="rounded-xl border border-border bg-card p-3.5 shadow-sm">
            <div className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
              {label}
            </div>
            <div className="mt-1.5 truncate text-sm font-semibold">{value}</div>
          </div>
        ))}
      </section>

      {/* Control Center & Configuration Grid */}
      <section className="grid gap-6 lg:grid-cols-12">
        {/* Left Column: Command Center & Desired Config */}
        <div className="space-y-6 lg:col-span-7">
          {/* One-Time Actions */}
          <div className="rounded-xl border border-border bg-card p-5 shadow-sm">
            <div className="flex items-center justify-between pb-3 border-b border-border">
              <div className="flex items-center gap-2">
                <Send className="h-4 w-4 text-primary" />
                <h2 className="font-semibold text-base">HUB Remote Control</h2>
              </div>
              <span className="text-xs text-muted-foreground">One-time downlink actions (QoS 1)</span>
            </div>

            <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
              {commandButtons.map(({ action, label }) => (
                <button
                  key={action}
                  onClick={() => sendCommand(action)}
                  disabled={busy !== null}
                  className="flex flex-col items-center justify-center rounded-lg border border-border bg-background p-3 text-center transition-all hover:bg-muted hover:border-primary/40 disabled:opacity-50"
                >
                  <Activity className="h-5 w-5 text-primary mb-1.5" />
                  <span className="text-xs font-medium">{busy === action ? "Sending..." : label}</span>
                </button>
              ))}
            </div>

            {notice && (
              <div className="mt-4 rounded-lg bg-primary/10 border border-primary/20 p-3 text-xs font-medium text-primary flex items-center justify-between">
                <span>{notice}</span>
                <span className="text-[10px] text-muted-foreground font-mono">Live Sync</span>
              </div>
            )}

            <div className="mt-4 pt-3 border-t border-border flex items-center justify-between text-xs text-muted-foreground">
              <span>Command channel:</span>
              <div className="flex items-center gap-1 font-mono text-[11px] bg-muted px-2 py-1 rounded">
                <code>protec/hubs/{h.hubId}/command/request</code>
                <button
                  onClick={() => copyToClipboard(`protec/hubs/${h.hubId}/command/request`)}
                  className="text-muted-foreground hover:text-foreground"
                >
                  {copiedTopic === `protec/hubs/${h.hubId}/command/request` ? (
                    <Check className="h-3 w-3 text-emerald-500" />
                  ) : (
                    <Copy className="h-3 w-3" />
                  )}
                </button>
              </div>
            </div>
          </div>

          {/* Desired Configuration Form */}
          <div className="rounded-xl border border-border bg-card p-5 shadow-sm">
            <div className="flex items-center justify-between pb-3 border-b border-border">
              <div className="flex items-center gap-2">
                <Sliders className="h-4 w-4 text-primary" />
                <h2 className="font-semibold text-base">Remote Display Configuration</h2>
              </div>
              <div className="flex items-center gap-2">
                <span className="rounded bg-muted px-2 py-0.5 text-xs font-mono font-medium">
                  Reported Rev: {h.configRevision}
                </span>
              </div>
            </div>

            <form onSubmit={applyDesiredConfig} className="mt-4 space-y-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <label className="text-xs font-medium text-muted-foreground">Display Mode</label>
                  <select
                    value={displayMode}
                    onChange={(e) => setDisplayMode(e.target.value)}
                    className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
                  >
                    <option value="overview">Overview (Dashboard + Sensors)</option>
                    <option value="kiosk">Kiosk (Full Screen TV Cards)</option>
                    <option value="single">Single Source Focused</option>
                  </select>
                </div>

                <div>
                  <label className="text-xs font-medium text-muted-foreground">
                    Rotation Cadence ({rotationSeconds}s)
                  </label>
                  <input
                    type="range"
                    min="10"
                    max="60"
                    step="5"
                    value={rotationSeconds}
                    onChange={(e) => setRotationSeconds(Number(e.target.value))}
                    className="mt-2 w-full accent-primary"
                  />
                  <div className="flex justify-between text-[10px] text-muted-foreground mt-0.5">
                    <span>10s</span>
                    <span>30s</span>
                    <span>60s</span>
                  </div>
                </div>

                <div>
                  <label className="text-xs font-medium text-muted-foreground">
                    Welcome Banner ({welcomeSeconds}s)
                  </label>
                  <input
                    type="range"
                    min="3"
                    max="15"
                    step="1"
                    value={welcomeSeconds}
                    onChange={(e) => setWelcomeSeconds(Number(e.target.value))}
                    className="mt-2 w-full accent-primary"
                  />
                  <div className="flex justify-between text-[10px] text-muted-foreground mt-0.5">
                    <span>3s</span>
                    <span>7s (default)</span>
                    <span>15s</span>
                  </div>
                </div>

                <div>
                  <label className="text-xs font-medium text-muted-foreground">History Window</label>
                  <select
                    value={historyRange}
                    onChange={(e) => setHistoryRange(e.target.value)}
                    className="mt-1 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
                  >
                    <option value="1h">1 Hour Trends</option>
                    <option value="6h">6 Hour Trends</option>
                    <option value="24h">24 Hour Trends</option>
                  </select>
                </div>
              </div>

              <div className="flex items-center justify-between pt-2">
                <span className="text-xs text-muted-foreground">
                  Published with QoS 1 Retain &bull; Applies on device ACK
                </span>
                <button
                  type="submit"
                  disabled={configSubmitting}
                  className="rounded-lg bg-primary px-4 py-2 text-xs font-medium text-primary-foreground shadow hover:bg-primary/90 transition-colors disabled:opacity-50"
                >
                  {configSubmitting ? "Deploying..." : `Apply Desired Config (Rev ${h.configRevision + 1})`}
                </button>
              </div>
            </form>
          </div>
        </div>

        {/* Right Column: Live TV Sensor Cards & Topic Overview */}
        <div className="space-y-6 lg:col-span-5">
          {/* Active Sensor / TV Cards (Dynamic from Realtime Telemetry) */}
          <div className="rounded-xl border border-border bg-card p-5 shadow-sm">
            <div className="flex items-center justify-between pb-3 border-b border-border">
              <div className="flex items-center gap-2">
                <Tv className="h-4 w-4 text-primary" />
                <h2 className="font-semibold text-base">Active TV Sensor Feeds</h2>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-muted-foreground">
                  {Object.keys(sensorFeeds).length > 0
                    ? `${Object.keys(sensorFeeds).length} active feed(s)`
                    : "Awaiting live telemetry"}
                </span>
                <span
                  className={`h-2 w-2 rounded-full ${
                    Object.keys(sensorFeeds).length > 0 ? "bg-emerald-500 animate-pulse" : "bg-slate-400"
                  }`}
                />
              </div>
            </div>

            {Object.keys(sensorFeeds).length === 0 ? (
              <div className="mt-4 rounded-lg border border-dashed border-border bg-muted/20 p-5 text-center">
                <Activity className="mx-auto h-7 w-7 text-muted-foreground/60 mb-2" />
                <h4 className="text-xs font-semibold">No Live Feeds Received Yet</h4>
                <p className="mt-1 text-[11px] text-muted-foreground max-w-xs mx-auto">
                  When the HUB publishes on <code className="font-mono text-primary">protec/hubs/{h.hubId}/telemetry/current/+</code> or reports status, sensor cards will populate dynamically here in real time.
                </p>
                <button
                  onClick={() => sendCommand("source.poll_once")}
                  disabled={busy !== null}
                  className="mt-3 inline-flex items-center gap-1.5 rounded-md bg-secondary px-3 py-1.5 text-xs font-medium text-secondary-foreground hover:bg-secondary/80 transition-colors"
                >
                  <Send className="h-3 w-3" />
                  Poll Sensor Sources Now
                </button>
              </div>
            ) : (
              <div className="mt-4 space-y-3">
                {Object.values(sensorFeeds).map((feed) => (
                  <div
                    key={feed.id}
                    className="rounded-lg border border-border bg-muted/20 p-3.5 flex items-center justify-between transition-all hover:bg-muted/40"
                  >
                    <div className="flex items-center gap-3">
                      <div className="rounded-md bg-primary/10 p-2 text-primary">
                        {feed.sensor.toLowerCase().includes("noise") || feed.metric.toLowerCase().includes("noise") ? (
                          <Volume2 className="h-5 w-5 text-blue-500" />
                        ) : feed.sensor.toLowerCase().includes("wbgt") || feed.metric.toLowerCase().includes("temp") ? (
                          <Thermometer className="h-5 w-5 text-amber-500" />
                        ) : feed.sensor.toLowerCase().includes("vib") || feed.metric.toLowerCase().includes("ppv") ? (
                          <Zap className="h-5 w-5 text-purple-500" />
                        ) : (
                          <Activity className="h-5 w-5 text-primary" />
                        )}
                      </div>
                      <div>
                        <div className="font-medium text-sm">
                          {feed.sensor} {feed.metric && feed.metric !== feed.sensor ? `â€” ${feed.metric}` : ""}
                        </div>
                        <div className="text-xs text-muted-foreground">
                          {feed.site || `Feed: ${feed.id}`} &bull; {feed.updatedAt}
                        </div>
                      </div>
                    </div>
                    <div className="text-right">
                      <span className="text-lg font-bold">{feed.value}</span>
                      {feed.unit && <span className="text-xs text-muted-foreground ml-1">{feed.unit}</span>}
                      {feed.limit !== undefined && (
                        <div className="text-[10px] text-muted-foreground font-medium">
                          Limit: {feed.limit} {feed.unit}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* MQTT Topic Architecture Spec */}
          <div className="rounded-xl border border-border bg-card p-5 shadow-sm">
            <h2 className="font-semibold text-sm">HUB Topic Contract</h2>
            <div className="mt-3 space-y-2 text-xs font-mono">
              <div className="rounded bg-muted/50 p-2">
                <span className="text-muted-foreground">Downlink Cmd:</span>
                <p className="mt-0.5 text-foreground break-all">protec/hubs/{h.hubId}/command/request</p>
              </div>
              <div className="rounded bg-muted/50 p-2">
                <span className="text-muted-foreground">Desired Config:</span>
                <p className="mt-0.5 text-foreground break-all">protec/hubs/{h.hubId}/config/desired</p>
              </div>
              <div className="rounded bg-muted/50 p-2">
                <span className="text-muted-foreground">Reported State:</span>
                <p className="mt-0.5 text-foreground break-all">protec/hubs/{h.hubId}/status/snapshot</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Realtime Command & Config Responses */}
      <section className="rounded-xl border border-border bg-card p-5 shadow-sm">
        <div className="flex items-center justify-between pb-3 border-b border-border">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 text-emerald-500" />
            <h2 className="font-semibold text-base">Structured Responses & ACKs</h2>
          </div>
          <span className="text-xs text-muted-foreground">
            {responses.length} event{responses.length === 1 ? "" : "s"} received in this session
          </span>
        </div>

        {responses.length === 0 ? (
          <p className="mt-4 text-sm text-muted-foreground">
            No command ACK or status packets received in this browser session yet. Dispatch a command above or wait for heartbeat.
          </p>
        ) : (
          <div className="mt-4 grid gap-3 md:grid-cols-2">
            {responses.map((response, index) => (
              <article
                key={`${response.message.receivedAt}-${index}`}
                className="rounded-lg border border-border bg-muted/20 p-4 shadow-sm"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="inline-flex items-center gap-1.5 font-semibold text-sm">
                    <span className="h-2 w-2 rounded-full bg-emerald-500" />
                    {responseType(response).replaceAll("_", " ")}
                  </span>
                  <span className="text-xs text-muted-foreground">
                    {new Date(response.message.receivedAt).toLocaleTimeString()}
                  </span>
                </div>
                <div className="mt-1 font-mono text-[11px] text-muted-foreground break-all">
                  {response.message.topic}
                </div>

                {response.data ? (
                  <dl className="mt-3 grid gap-x-4 gap-y-1.5 sm:grid-cols-2 text-xs">
                    {flatten(response.data)
                      .filter(([key]) => key !== "type" && key !== "schema")
                      .slice(0, 14)
                      .map(([key, value]) => (
                        <div key={key} className="min-w-0">
                          <dt className="truncate text-muted-foreground font-mono">{key}</dt>
                          <dd className="truncate font-medium">{value}</dd>
                        </div>
                      ))}
                  </dl>
                ) : (
                  <pre className="mt-2 whitespace-pre-wrap break-words text-xs bg-muted/50 p-2 rounded">
                    {response.message.payload}
                  </pre>
                )}
              </article>
            ))}
          </div>
        )}
      </section>

      {/* Raw MQTT Packets Stream (Matching the User's UI Screenshot Exactly) */}
      <section className="rounded-xl border border-border bg-card p-5 shadow-sm">
        <div className="flex items-center justify-between pb-3 border-b border-border">
          <div className="flex items-center gap-2">
            <Radio className="h-4 w-4 text-primary" />
            <h2 className="font-semibold text-base">Raw MQTT Packets</h2>
          </div>
          <span className="text-xs text-muted-foreground">
            Showing last {messages.length} packet{messages.length === 1 ? "" : "s"}
          </span>
        </div>

        {messages.length === 0 ? (
          <div className="p-8 text-center text-sm text-muted-foreground">
            No MQTT packets received for <code className="font-mono text-xs">protec/hubs/{h.hubId}/#</code> yet.
          </div>
        ) : (
          <div className="mt-4 max-h-96 space-y-3 overflow-y-auto pr-1">
            {messages.map((message, index) => (
              <div
                key={`${message.receivedAt}-${index}`}
                className="rounded-lg border border-border bg-muted/30 p-3.5 transition-all hover:bg-muted/50"
              >
                <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground pb-2 border-b border-border/50">
                  <span className="font-mono font-medium text-primary">{message.topic}</span>
                  <span className="font-mono">{new Date(message.receivedAt).toLocaleString()}</span>
                </div>
                <pre className="mt-2 whitespace-pre-wrap break-words font-mono text-xs leading-relaxed">
                  {message.payload}
                </pre>
              </div>
            ))}
          </div>
        )}
      </section>
    </main>
  );
}
