"use client";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Activity, BarChart3, CheckCircle2, Image as ImageIcon, Layers, Loader2, Radio, RefreshCw, Send, Server, Settings2, ShieldCheck, Signal, Terminal, type LucideIcon } from "lucide-react";
import { HubsApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { useAuth } from "@/lib/auth/AuthContext";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { Modal } from "@/components/ui/Modal";
import { parseSnapshot, type CapturedMessage, type HubCommand, type HubMonitor, type HubSnapshot } from "@/lib/hubs";
import { age, date, duration, Badge, Panel, CopyButton, JsonView } from "./ConsoleWidgets";
import { CommandParameters, defaultData, labels, operations, readOnly } from "./CommandParameters";
import { ActivityPanels } from "./ActivityPanels";
import { SensorPanel } from "./SensorPanel";
import "./hub-console.css";

export function HubConsole({ id }: { id: string }) {
  const hub = useApi(() => HubsApi.get(id), [id]);
  const { user } = useAuth();
  const [monitor, setMonitor] = useState<HubMonitor | null>(null);
  const [commands, setCommands] = useState<HubCommand[]>([]);
  const [pollError, setPollError] = useState("");
  const [now, setNow] = useState(() => Date.now());
  const [op, setOp] = useState("get");
  const [data, setData] = useState<Record<string, unknown>>({});
  const [requestId, setRequestId] = useState("");
  const [baseVersion, setBaseVersion] = useState<number | undefined>();
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [confirm, setConfirm] = useState(false);
  const [paramsOpen, setParamsOpen] = useState(false);
  const [liveSnapshot, setLiveSnapshot] = useState<HubSnapshot | null>(null);
  const [sessionEvents, setSessionEvents] = useState<Array<{ time: number; signal?: number }>>([]);
  const mounted = useRef(true);
  const refreshInFlight = useRef(false);
  const h = hub.data;
  const stored = parseSnapshot(h?.lastStatusJson);
  const snapshot = liveSnapshot && (!stored?.time_utc || (liveSnapshot.time_utc ?? "") >= stored.time_utc) ? liveSnapshot : stored;
  const refresh = useCallback(async () => {
    if (refreshInFlight.current) return;
    refreshInFlight.current = true;
    try {
      const start = new Date(); start.setHours(0, 0, 0, 0);
      const [m, c] = await Promise.all([HubsApi.monitor(id, start.toISOString()), HubsApi.commands(id)]);
      if (mounted.current) { setMonitor(m); setCommands(c); setPollError(""); }
    } catch (e) { if (mounted.current) setPollError(e instanceof Error ? e.message : "Monitoring refresh failed"); }
    finally { refreshInFlight.current = false; }
  }, [id]);
  const prepare = useCallback(async () => {
    try { const r = await HubsApi.prepare(id); if (mounted.current) setRequestId(r.requestId); }
    catch (e) { if (mounted.current) setNotice(e instanceof Error ? e.message : "Cannot allocate request ID"); }
  }, [id]);
  useEffect(() => {
    mounted.current = true;
    void refresh(); void prepare();
    const timer = setInterval(() => setNow(Date.now()), 1000);
    const polling = setInterval(() => { if (!document.hidden) void refresh(); }, 5000);
    const records = setInterval(() => { if (!document.hidden) hub.refetch(); }, 20000);
    return () => { mounted.current = false; clearInterval(timer); clearInterval(polling); clearInterval(records); };
  }, [refresh, prepare, hub.refetch]);
  useRealtime({ HubMessageReceived: (value: unknown) => {
    const m = value as CapturedMessage & { hubId: string };
    if (!m || m.hubId !== h?.hubId) return;
    const suffix = m.topic.split("/").pop();
    if (!["heartbeat", "snapshot", "ack"].includes(suffix ?? "")) return;
    const captured = { ...m, bytes: new TextEncoder().encode(m.payload).length };
    setMonitor(prev => prev ? { ...prev, messages: [...prev.messages.filter(p => p.topic !== m.topic), captured] } : prev);
    let packet: Record<string, unknown>;
    try { packet = JSON.parse(m.payload); } catch { return; }
    if (suffix === "snapshot") { const s = parseSnapshot(m.payload); if (s) setLiveSnapshot(s); }
    if (suffix !== "ack") hub.refetch();
    if (suffix === "ack" || suffix === "snapshot") void refresh();
    const signal = (packet.network as { signal_percent?: number })?.signal_percent;
    setSessionEvents(prev => [...prev, { time: new Date(m.receivedAt).getTime(), signal: typeof signal === "number" ? signal : undefined }].filter(p => p.time >= Date.now() - 900000).slice(-2000));
  } });
  function choose(next: string) {
    setOp(next); setData(defaultData(next, snapshot)); setBaseVersion(snapshot?.config_version); setNotice("");
  }
  const messageFor = (suffix: string) => monitor?.messages.find(m => m.topic.endsWith(`/${suffix}`));
  const heartbeat = messageFor("heartbeat");
  let heartbeatData: Partial<HubSnapshot> & { online?: boolean } = {};
  try { heartbeatData = JSON.parse(heartbeat?.payload ?? "{}"); } catch { /* unavailable */ }
  const heartbeatAge = age(heartbeatData.time_utc ?? h?.lastHeartbeatAt, now);
  const snapshotAge = age(snapshot?.time_utc, now);
  const connectivity = heartbeatData.online === false || h?.status === "Offline" ? "Offline" : h?.status === "Standalone" ? "Standalone" : h?.status === "Online" && heartbeatAge !== null && heartbeatAge <= 300 ? "Online" : "Stale";
  const writesEnabled = !!h?.remoteApplyEnabled && connectivity === "Online" && snapshot?.v === 2 && snapshotAge != null && snapshotAge <= 300;
  const network = heartbeatData.network ?? snapshot?.network;
  const television = { ...snapshot?.tv, ...heartbeatData.tv };
  const preview = { v: 2, id: requestId || "Allocating request ID…", op, ...(!readOnly(op) ? { if_version: baseVersion } : {}), ...(Object.keys(data).length || op === "api.refresh" ? { data } : {}) };
  async function submit() {
    setConfirm(false); setBusy(true); setNotice("");
    try {
      const res = await HubsApi.command(id, op, data, readOnly(op) ? undefined : baseVersion, requestId);
      setNotice(`${res.action} sent. Waiting for the HUB acknowledgement (${res.commandId}).`);
      await prepare(); await refresh();
    } catch (e) { setNotice(e instanceof Error ? e.message : "Command dispatch failed"); await prepare(); }
    finally { if (mounted.current) setBusy(false); }
  }
  if (!h && hub.loading) return <div className="mqtt-loading" role="status"><div className="mqtt-skeleton" /><div className="mqtt-skeleton" /><div className="mqtt-skeleton large" /><span>Loading HUB monitoring…</span></div>;
  if (!h) return <div className="mqtt-warning" role="alert">{hub.error || "HUB not found"}<button onClick={hub.refetch}>Retry</button></div>;
  const kpis: Array<{ icon: LucideIcon; label: string; value: string; hint?: string; good?: boolean }> = [
    { icon: Radio, label: "HUB Status", value: connectivity, good: connectivity === "Online" },
    { icon: ShieldCheck, label: "Cloud broker TLS", value: monitor ? monitor.broker.tls ? "Enabled" : "Disabled" : "Unavailable", hint: "Backend connection; device TLS is reported separately" },
    { icon: Server, label: "Broker connection", value: monitor ? monitor.broker.connected ? "Connected" : "Disconnected" : "Unavailable", hint: monitor ? `${monitor.broker.host}:${monitor.broker.port} · Cloud connection` : undefined },
    { icon: Layers, label: "Management QoS", value: "QoS 1", hint: "At least once delivery" },
    { icon: Activity, label: "Heartbeat age", value: duration(heartbeatAge), good: heartbeatAge != null && heartbeatAge < 300 },
    { icon: ImageIcon, label: "Snapshot age", value: duration(snapshotAge) },
    { icon: Terminal, label: "Commands today", value: monitor?.commandsToday.toString() ?? "—", hint: "Your local calendar day" },
    { icon: CheckCircle2, label: "Success rate today", value: monitor?.successRate != null ? `${monitor.successRate}%` : "—", hint: "Confirmed results / terminal requests" },
  ];
  const topics = [{ suffix: "heartbeat", icon: Activity, retained: true }, { suffix: "snapshot", icon: ImageIcon, retained: true }, { suffix: "config", icon: Settings2, retained: false }, { suffix: "ack", icon: CheckCircle2, retained: false }];
  const details: Array<[string, ReactNode]> = [
    ["Network type", network?.type ?? "—"], ["Active interface", network?.interface ?? "—"], ["Signal strength", network?.signal_percent != null ? `${network.signal_percent}%` : "—"],
    ["Internet status", network?.internet === true ? <Badge state="Online" /> : network?.internet === false ? <Badge state="Offline" /> : "Unavailable"],
    ["Selected sensors", television.selected ?? "—"], ["Live sensors", television.live ?? "—"], ["Showing sensors", television.showing ?? "—"],
    ["App version", snapshot?.firmware ?? h.appVersion ?? "—"], ["Agent version", h.agentVersion ?? "—"],
    ["Runtime health", snapshot?.system?.runtime_healthy === true ? "Healthy" : snapshot?.system?.runtime_healthy === false ? "Unhealthy" : "—"],
  ];
  const signals = sessionEvents.filter(e => e.signal != null);
  const bins = Array.from({ length: 15 }, (_, i) => sessionEvents.filter(e => Math.floor((now - e.time) / 60000) === 14 - i).length);
  return <>
    <header className="mqtt-identity"><div className="mqtt-identity-icon"><Server size={23} /></div><div><p className="mqtt-eyebrow">MQTT CONTROL & MONITOR</p><div className="mqtt-title-row"><h1>{h.name || `HUB-${h.hubId}`}</h1><Badge state={connectivity} /></div><p className="mqtt-subtitle">{h.hubId} · {h.tenantName || "Unassigned tenant"} · {snapshot?.site?.name || h.siteName || "Unassigned site"}</p></div><div className="mqtt-header-right"><span>Last heartbeat: <strong>{date(heartbeatData.time_utc ?? h.lastHeartbeatAt)}</strong></span><button onClick={() => { hub.refetch(); void refresh(); }} disabled={hub.loading}><RefreshCw size={14} className={hub.loading ? "animate-spin" : ""} /> Refresh</button><span className="mqtt-admin">{user?.displayName ?? "Administrator"}</span></div></header>
    {(pollError || hub.error) && <div className="mqtt-warning" role="alert">Live refresh interrupted: {pollError || hub.error}. Last available data is shown.</div>}
    {connectivity !== "Online" && <div className="mqtt-warning">HUB is {connectivity.toLowerCase()}. A retained message alone does not establish a live connection. Configuration writes are disabled.</div>}
    {snapshotAge != null && snapshotAge > 300 && <div className="mqtt-warning">Snapshot is {duration(snapshotAge)} old. Sensor readings and configuration may be out of date. Request a fresh snapshot before making changes.</div>}
    <div className="mqtt-kpis">{kpis.map(({ icon: Icon, label, value, hint, good }) => <div className={`mqtt-kpi ${good ? "positive" : ""}`} key={label} title={hint}><Icon size={24} /><div><strong>{value}</strong><span>{label}</span>{label === "Broker connection" && hint && <small>{hint}</small>}</div></div>)}</div>
    <section className="mqtt-topics"><div className="mqtt-topics-title"><Layers size={19} /><h2>MQTT Topics</h2><span>Management topics for this HUB · QoS 1</span></div><div className="mqtt-topic-grid">{topics.map(({ suffix, icon: Icon, retained }) => { const topic = `protec/hubs/${h.hubId}/${suffix}`; const when = suffix === "config" ? commands[0]?.issuedAt : messageFor(suffix)?.receivedAt; return <article className={`mqtt-topic ${suffix === "config" ? "command" : ""}`} key={suffix}><Icon size={25} /><div><div className="mqtt-topic-name"><strong>{topic}</strong><CopyButton text={topic} /></div><div className="mqtt-topic-tags"><span>{suffix === "config" ? "Cloud → HUB" : "HUB → Cloud"}</span><span title="At least once: duplicate delivery is possible">QoS 1</span><span className={retained ? "retained" : "not-retained"}>Retained {retained ? "ON" : "OFF"}</span></div><small title={date(when)}>{when ? `${suffix === "config" ? "Sent" : "Received"} ${duration(age(when, now))} ago` : "No message captured"}</small></div></article>; })}</div></section>
    <div className="mqtt-main-grid">
      <Panel icon={BarChart3} title="Live Monitoring" subtitle="Real-time telemetry and MQTT health" extra={<Badge state={connectivity}>{connectivity === "Online" ? "Live" : connectivity}</Badge>}>
        <div className="mqtt-monitor-split"><dl className="mqtt-metrics">{details.map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}</dl><div className="mqtt-trends"><div className="mqtt-trend-title">Signal strength <span>Observed this session</span></div>{signals.length >= 2 ? <svg viewBox="0 0 280 90" role="img" aria-label="Actual signal strength samples from this browser session"><polyline points={signals.map(e => `${Math.min(280, Math.max(0, (e.time - (now - 900000)) / 900000 * 280))},${90 - Math.min(100, Math.max(0, e.signal!)) * .85}`).join(" ")} stroke="#126448" fill="none" strokeWidth="2" /></svg> : <div className="mqtt-chart-empty"><Signal size={25} /><span>Waiting for live signal samples</span><small>No stored signal history is available.</small></div>}<div className="mqtt-trend-title">MQTT activity <span>{sessionEvents.filter(e => now - e.time <= 900000).length} observed messages</span></div><div className="mqtt-session-bars" aria-label="Real messages observed per minute in this session">{sessionEvents.length ? bins.map((count, i) => <div key={i} title={`${14 - i} minutes ago: ${count} messages`} style={{ height: `${count / Math.max(1, ...bins) * 100}%` }} />) : <span>Waiting for live MQTT events</span>}</div><div className="mqtt-chart-axis"><span>15 minutes ago</span><span>Now</span></div></div></div>
        <div className="mqtt-monitor-footer"><span>HUB time: {date(snapshot?.time_utc)}</span><span>Cloud receipt: {date(messageFor("snapshot")?.receivedAt)}</span></div>
      </Panel>
      <Panel icon={Send} title="Command Center" subtitle="Send MQTT commands and manage HUB operations" extra={<span>V2 RC1</span>}>
        <div className="mqtt-actions">{["get", "api.refresh", "site.set", "tv.select", "display.set", "threshold.set", "api.test", "api.set"].map(action => <button className={op === action ? "active" : ""} key={action} onClick={() => { choose(action); if (!["get", "api.refresh"].includes(action)) setParamsOpen(true); }} disabled={busy || (!readOnly(action) && !writesEnabled)} title={!readOnly(action) && !writesEnabled ? "Requires a verified apply-enabled HUB" : labels[action]}>{action === "get" ? <ImageIcon size={14} /> : action === "api.refresh" ? <RefreshCw size={14} /> : <Settings2 size={14} />}{labels[action]}</button>)}</div>
        <div className="mqtt-composer"><div className="mqtt-composer-fields"><label>Request ID<div className="mqtt-readonly" title={requestId}>{requestId || "Allocating…"}{requestId && <CopyButton text={requestId} />}</div></label><label>Operation<select value={op} disabled={busy} onChange={e => choose(e.target.value)}>{operations.map(action => <option key={action} value={action} disabled={!readOnly(action) && !writesEnabled}>{labels[action]} · {action}</option>)}</select></label><label title="Integer configuration version from the latest authoritative snapshot">if_version<input readOnly value={readOnly(op) ? "Not required" : baseVersion ?? "Snapshot required"} /></label>{Object.keys(data).length > 0 && <button disabled={busy} onClick={() => setParamsOpen(true)}><Settings2 size={14} /> Edit parameters</button>}<small>QoS 1 · Retain OFF<br />Application ACK required</small></div><div><div className="mqtt-payload-label">Payload (JSON)</div><JsonView value={preview} /></div></div>
        {!h.remoteApplyEnabled && <p className="mqtt-control-note">Read-only RC1: cloud apply is disabled until verified on the Pi. Configuration and logo controls require apply mode.</p>}
        {!readOnly(op) && baseVersion !== snapshot?.config_version && <p className="mqtt-warning">Configuration changed. Reload and review before sending.<button onClick={() => choose(op)}>Reload parameters</button></p>}
        <div className="mqtt-command-footer"><div className="mqtt-unsupported"><button disabled title="RC1 does not implement a restart helper">Restart Service</button><button disabled title="Remote network transactions are not implemented in RC1">Network</button><button disabled title="Remote OTA is not implemented in the supplied handoff">OTA</button></div><button className="mqtt-send" disabled={busy || !requestId || (!readOnly(op) && (!writesEnabled || baseVersion !== snapshot?.config_version))} onClick={() => readOnly(op) ? void submit() : setConfirm(true)}>{busy ? <Loader2 size={15} className="animate-spin" /> : <Send size={15} />}{busy ? "Submitting…" : "Send Request"}</button></div>
      </Panel>
    </div>
    {notice && <div className="mqtt-notice" role="status">{notice}</div>}
    <ActivityPanels hubId={h.hubId} commands={commands} messages={monitor?.messages ?? []} snapshot={snapshot} now={now} />
    <SensorPanel id={id} snapshot={snapshot} snapshotAge={snapshotAge} writesEnabled={writesEnabled && !busy} onNotice={setNotice} onRefresh={() => { hub.refetch(); void refresh(); }} />
    {paramsOpen && <CommandParameters op={op} data={data} setData={setData} version={baseVersion} snapshot={snapshot} snapshotAge={snapshotAge} onClose={() => setParamsOpen(false)} />}
    {confirm && <Modal title={`Confirm ${labels[op]}`} onClose={() => setConfirm(false)}><div className="mqtt-form"><p>Send this change to <strong>{h.name}</strong> using configuration version <strong>{baseVersion}</strong>? A device ACK and matching saved snapshot are required before showing Applied.</p><JsonView value={preview} /><button className="mqtt-send" onClick={() => void submit()}>Confirm & Send</button></div></Modal>}
  </>;
}
