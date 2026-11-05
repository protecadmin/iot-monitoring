"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import Link from "next/link";
import { Activity, ArrowLeft, Building2, ChevronRight, Clock3, Cpu, Database, FileText, History, Layers, Link2, Loader2, MapPin, Network, Radio, RefreshCw, Router, Save, Settings, Signal, Wifi, type LucideIcon } from "lucide-react";
import { StatusBadge } from "@/components/ui/States";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { useCan } from "@/lib/auth/permissions";
import type { GatewayDto } from "@/types/api";
import { gatewayStatusPresentation, latestGatewayStatus } from "@/lib/gateway-status";
import styles from "./GatewayMonitoringView.module.css";

type Data = Record<string, unknown>;
export type GatewayMessage = { gatewayCode: string; topic: string; receivedAt: string; payload: string };
export type GatewayResponse = { message: GatewayMessage; data: Data | null };
type Send = (op: string, payload?: Data) => Promise<void>;

function object(value: unknown): Data {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Data : {};
}
function decodedReading(reading: Data, sensorName: string): { values: Data; units: Data; interval?: string } {
  const values = reading.values;
  if (!Array.isArray(values)) return { values: object(values), units: object(reading.units) };
  const valid = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value) && value !== -2147483648;
  if (sensorName.toUpperCase() === "BSWA308" || sensorName === "1") {
    const result: Data = {};
    const units: Data = {};
    if (valid(values[0])) { result.LAeqT = values[0] / 10; units.LAeqT = "dB"; }
    return { values: result, units, interval: valid(values[3]) ? `${values[3]} s reporting interval` : undefined };
  }
  return { values: Object.fromEntries(values.map((value, index) => [`Value ${index + 1}`, value]).filter(([, value]) => valid(value))), units: {} };
}
function display(value: unknown): string {
  if (value === undefined || value === null || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  return typeof value === "object" ? JSON.stringify(value) : String(value);
}
function date(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  const parsed = new Date(typeof value === "number" ? value * 1000 : String(value));
  return Number.isNaN(parsed.getTime()) ? display(value) : parsed.toLocaleString();
}
function safe(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(safe);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, child]) => [key, /password|secret|token/i.test(key) ? "[redacted]" : safe(child)]));
  return value;
}
function raw(response: GatewayResponse): string {
  return response.data ? JSON.stringify(safe(response.data), null, 2) : response.message.payload;
}
function Panel({ id, title, description, icon: Icon, action, children, className = "" }: { id?: string; title: string; description?: string; icon: LucideIcon; action?: ReactNode; children: ReactNode; className?: string }) {
  return <section id={id} className={`${styles.panel} ${className}`}>
    <header className={styles.panelHeader}><Icon aria-hidden="true" /><div><h2>{title}</h2>{description && <p>{description}</p>}</div>{action && <div className={styles.panelAction}>{action}</div>}</header>
    {children}
  </section>;
}
function Metric({ icon: Icon, label, value, note }: { icon: LucideIcon; label: string; value: unknown; note?: ReactNode }) {
  return <div className={styles.metric}><Icon aria-hidden="true" /><div><span>{label}</span><strong>{display(value)}</strong>{note && <small>{note}</small>}</div></div>;
}
const COMMANDS: { op: string; label: string; icon: LucideIcon; target: string }[] = [
  { op: "get_status", label: "Status", icon: Activity, target: "gateway-status" },
  { op: "get_latest", label: "Latest Reading", icon: Signal, target: "gateway-latest" },
  { op: "get_config", label: "Configuration", icon: Settings, target: "gateway-config" },
  { op: "get_capabilities", label: "Capabilities", icon: Layers, target: "gateway-capabilities" },
  { op: "get_history", label: "History", icon: History, target: "gateway-history" },
  { op: "get_logs", label: "Logs", icon: FileText, target: "gateway-logs" },
  { op: "recover_cellular", label: "Recover Network", icon: RefreshCw, target: "gateway-responses" },
];

export function GatewayMonitoringView({ gateway: g, responses, messages, busy, notice, send, statusResponse: retainedStatus }: { gateway: GatewayDto; responses: GatewayResponse[]; messages: GatewayMessage[]; busy: string | null; notice: string | null; send: Send; statusResponse?: GatewayResponse }) {
  const [selected, setSelected] = useState("get_status");
  const can = useCan();
  const canSend = can("commands.send");
  const statusResponse = latestGatewayStatus([...(retainedStatus ? [retainedStatus] : []), ...responses]);
  const status = statusResponse?.data ?? {};
  const sensor = object(status.sensor);
  const health = gatewayStatusPresentation(status, g);
  const latest = responses.find(r => r.data?.type === "measurement" || r.data?.type === "latest");
  const reading = object(latest?.data?.record ?? latest?.data);
  const readingSensorName = typeof reading.sensor === "string" && reading.sensor.trim()
    ? reading.sensor
    : typeof sensor.name === "string" ? sensor.name : "";
  const decoded = decodedReading(reading, readingSensorName || String(reading.sensor ?? reading.sensor_id ?? ""));
  const values = decoded.values;
  const config = responses.find(r => typeof r.data?.mqtt_host === "string");
  const capabilities = responses.find(r => r.data?.capabilities !== undefined);
  const history = responses.find(r => r.data?.type === "history_page");
  const logs = responses.find(r => r.data?.type === "log_page");
  const disabled = busy !== null || !canSend;

  function activate(op: string, target: string) {
    setSelected(op);
    const section = document.getElementById(target);
    if (section instanceof HTMLDetailsElement) section.open = true;
    section?.scrollIntoView({ behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth", block: "nearest" });
    void send(op);
  }

  return <div className={styles.monitor}>
    <header className={styles.overview}>
      <Link className={styles.breadcrumb} href="/gateways"><ArrowLeft size={14} />Gateways<ChevronRight size={12} /><strong>Inspect Gateway</strong></Link>
      <div className={styles.identity}>
        <Router className={styles.gatewayIcon} aria-hidden="true" />
        <div className={styles.gatewayName}><h1>{g.name || `Gateway ${g.gatewayCode}`}</h1>{g.name && <small>{g.gatewayCode}</small>}</div>
        <StatusBadge status={g.status === "Standalone" ? "Unassigned" : g.status} />
        <div className={styles.meta}><Building2 /><div><span>Tenant</span><strong>{g.tenantName || "Unassigned"}</strong></div></div>
        <div className={styles.meta}><MapPin /><div><span>Site</span><strong>{g.siteName || "Unassigned"}</strong></div></div>
        <div className={styles.meta}><Clock3 /><div><span>Last Seen</span><strong>{date(g.lastSeenAt)}</strong></div></div>
      </div>
      <nav className={styles.commands} aria-label="Gateway monitoring commands">{COMMANDS.map(({ op, label, icon: Icon, target }) => <button key={op} type="button" disabled={disabled} aria-pressed={selected === op} onClick={() => activate(op, target)}>
        {busy === op ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Icon aria-hidden="true" />}<span>{label}</span>
      </button>)}</nav>
      {notice && <p className={styles.notice} role="status">{busy && <Loader2 size={14} className="animate-spin" />}{notice}</p>}
      {!canSend && <p className={styles.helper}>Your role has read-only access to gateway monitoring.</p>}
    </header>

    <div className={styles.primaryGrid}>
      <Panel id="gateway-status" icon={Activity} title="Gateway Status" description="Real-time summary of gateway health and resources" action={<small role="status">{statusResponse ? `Status received · ${date(statusResponse.message.receivedAt)}` : "Awaiting status"}</small>}>
        <div className={styles.metrics}>
          <Metric icon={Cpu} label="Firmware" value={health.firmware} note={health.firmwareNote} />
          <Metric icon={Radio} label="Connected Sensor" value={health.sensor} note={health.sensorNote} />
          <Metric icon={Network} label="Network" value={health.network} note={health.networkNote} />
          <Metric icon={Signal} label="Signal Strength" value={health.signal} note={health.signalNote} />
          <Metric icon={Wifi} label="IP Address" value={health.ip} />
          <Metric icon={Clock3} label="Uptime" value={health.uptime} />
          <Metric icon={Database} label="Storage" value={health.storage} note={health.storageNote} />
          <Metric icon={MapPin} label="GPS" value={health.gps} note={health.gpsNote} />
        </div>
        <div className={styles.statusFoot}><span>Battery <strong>{g.batteryPct == null ? "—" : `${g.batteryPct}%`}</strong></span><span>Last measurement <strong>{date(g.lastMeasurementAt)}</strong></span></div>
        {health.diagnostics.length > 0 && <details className={styles.statusDiagnostics}><summary>Network &amp; system diagnostics</summary><dl>{health.diagnostics.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{display(value)}</dd></div>)}</dl></details>}
      </Panel>
      <Panel id="gateway-latest" icon={Signal} title="Latest Sensor Reading" description="Most recent data from the connected device" action={latest && <StatusBadge status="Received" />}>
        <div className={styles.sensorCard}><Cpu aria-hidden="true" /><div><h3>{display(readingSensorName || (reading.sensor_id === 1 ? "BSWA308" : reading.sensor_id))}</h3><p>Connected sensor</p></div></div>
        {latest ? <><dl className={styles.readings}>{Object.keys(values).length > 0 ? Object.entries(values).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{display(value)} <small>{display(decoded.units[key]) === "—" ? "" : display(decoded.units[key])}</small></dd></div>) : <div><dt>Reported values</dt><dd>{display(reading.values)}</dd></div>}</dl>{decoded.interval && <p className={styles.helper}>{decoded.interval}</p>}<p className={styles.received}><Clock3 size={18} /><span>Received At<strong>{date(latest.message.receivedAt)}</strong></span></p></> : <p className={styles.empty}>No reading received in this session. Request Latest Reading to retrieve the gateway’s current data.</p>}
      </Panel>
    </div>

    <div className={styles.primaryGrid}>
      <Configuration key={`${g.id}-${config?.message.receivedAt ?? "waiting"}`} config={config?.data} send={send} disabled={disabled} canEdit={can("configuration.edit")} />
      <Panel icon={Link2} title="Mapped Device" description="Devices connected to this gateway">
        {g.deviceNames?.length ? <div className={styles.deviceList}>{g.deviceNames.map(name => <div className={styles.sensorCard} key={name}><Cpu aria-hidden="true" /><div><h3>{name}</h3><p>Mapped device</p></div></div>)}</div> : <p className={styles.empty}>No devices mapped. This gateway is standalone.</p>}
        <dl className={styles.deviceFacts}><div><dt><MapPin size={14} />Site</dt><dd>{g.siteName || "Unassigned"}</dd></div><div><dt><Router size={14} />Gateway ID</dt><dd>{g.gatewayCode}</dd></div></dl>
        <details className={styles.channel}><summary>Command channel</summary><CodeBlock value={`protec/gateway/${g.gatewayCode}/command`} /></details>
      </Panel>
    </div>

    <div className={styles.diagnosticsGrid}>
      <Diagnostics key={`history-${g.id}`} kind="history" response={history} send={send} disabled={disabled} />
      <Diagnostics key={`logs-${g.id}`} kind="logs" response={logs} send={send} disabled={disabled} />
      <Panel id="gateway-responses" icon={Layers} title="Gateway Responses" description="Latest command responses from the gateway">
        <div className={styles.responseList}>{responses.length ? responses.map((response, index) => <ResponseCard key={`${response.message.receivedAt}-${index}`} response={response} />) : <p className={styles.empty}>No response received in this session yet.</p>}</div>
      </Panel>
    </div>
    <details id="gateway-capabilities" className={styles.extra}><summary><Layers size={17} />Gateway capabilities</summary>{capabilities ? <CodeBlock value={JSON.stringify(safe(capabilities.data?.capabilities), null, 2)} /> : <p className={styles.empty}>Request Capabilities to retrieve supported gateway features.</p>}</details>
    <details className={styles.extra}><summary><FileText size={17} />Raw MQTT packets <small>{messages.length} packets in this session</small></summary>{messages.length ? messages.map((message, index) => {
      const matchingResponse = responses[index]?.message.topic === message.topic
        ? responses[index]
        : responses.find(response => response.message === message || (response.message.topic === message.topic && response.message.receivedAt === message.receivedAt))
          ?? responses.find(response => response.message.topic === message.topic && response.data !== null);
      return <article key={`${message.receivedAt}-${index}`} className={styles.packet}><p>{message.topic} · {date(message.receivedAt)}</p><CodeBlock value={raw(matchingResponse ?? { message, data: null })} /></article>;
    }) : <p className={styles.empty}>No packets received in this session yet.</p>}</details>
  </div>;
}

function Configuration({ config, send, disabled, canEdit }: { config?: Data | null; send: Send; disabled: boolean; canEdit: boolean }) {
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!config || disabled || !canEdit) return;
    const form = event.currentTarget;
    const fields = new FormData(form);
    const payload: Data = {
      mqtt_host: String(fields.get("mqtt_host")).trim(), mqtt_port: Number(fields.get("mqtt_port")),
      tls: fields.get("tls") === "on", mqtt_topic_root: String(fields.get("mqtt_topic_root")).trim(),
      backhaul_mode: Number(fields.get("backhaul_mode")), heartbeat_sec: Number(fields.get("heartbeat_sec")), status_sec: Number(fields.get("status_sec")),
    };
    const password = String(fields.get("mqtt_password") ?? "");
    if (password) { payload.mqtt_username = String(fields.get("mqtt_username") ?? ""); payload.mqtt_password = password; }
    void send("set_config", payload);
    const passwordInput = form.elements.namedItem("mqtt_password") as HTMLInputElement | null;
    if (passwordInput) passwordInput.value = "";
  }
  return <Panel id="gateway-config" icon={Settings} title="Gateway Configuration" description="View and update gateway communication settings" action={<div className={styles.actions}><button type="button" disabled={disabled} onClick={() => void send("get_config")}><RefreshCw size={14} />Reload Configuration</button><button form="gateway-config-form" type="submit" className={styles.primaryButton} disabled={disabled || !config || !canEdit}><Save size={14} />Save Configuration</button></div>}>
    {!config && <p className={styles.helper}>Reload Configuration to retrieve the gateway’s current settings before editing.</p>}
    <form id="gateway-config-form" onSubmit={save}>
      <fieldset disabled={disabled || !config || !canEdit} className={styles.fields}>
        <label>Broker Host <span>*</span><input name="mqtt_host" required defaultValue={String(config?.mqtt_host ?? "")} /></label>
        <label>Port <span>*</span><input name="mqtt_port" type="number" min={1} max={65535} required defaultValue={config?.mqtt_port == null ? "" : Number(config.mqtt_port)} /></label>
        <label className={styles.toggle}>TLS (Secure)<span><input name="tls" type="checkbox" defaultChecked={config?.tls === true} />Enable TLS</span></label>
        <label>Username<input name="mqtt_username" defaultValue={String(config?.mqtt_username ?? "")} /><small>Updated only when a new password is entered.</small></label>
        <label>Password<input name="mqtt_password" type="password" autoComplete="new-password" placeholder="••••••••" /><small>Leave unchanged to keep the existing password.</small></label>
        <label>Topic Root <span>*</span><input name="mqtt_topic_root" required defaultValue={String(config?.mqtt_topic_root ?? "")} /></label>
        <label>Backhaul Mode<select name="backhaul_mode" defaultValue={config?.backhaul_mode == null ? "" : String(config.backhaul_mode)} required><option value="" disabled>Select mode</option><option value="2">Auto</option><option value="0">Cellular (4G)</option><option value="1">Wi-Fi</option></select></label>
        <div className={styles.intervalFields}><label>Heartbeat Interval<input name="heartbeat_sec" type="number" min={1} required defaultValue={config?.heartbeat_sec == null ? "" : Number(config.heartbeat_sec)} /><small>seconds</small></label><label>Status Interval<input name="status_sec" type="number" min={1} required defaultValue={config?.status_sec == null ? "" : Number(config.status_sec)} /><small>seconds</small></label></div>
      </fieldset>
    </form>
  </Panel>;
}

function ResponseCard({ response }: { response: GatewayResponse }) {
  const data = response.data ?? {};
  const type = display(data.type ?? response.message.topic.split("/").pop()).replaceAll("_", " ");
  const fields = Object.entries(data).filter(([key, value]) => key !== "type" && typeof value !== "object" && !/password|secret|token/i.test(key));
  return <article className={styles.response}><div className={styles.responseTitle}><strong>{type}</strong><time>{date(response.message.receivedAt)}</time></div>{fields.length > 0 && <dl>{fields.map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{display(value)}</dd></div>)}</dl>}<details><summary>Raw MQTT Message (complete)</summary><CodeBlock value={raw(response)} /></details></article>;
}

function Diagnostics({ kind, response, send, disabled }: { kind: "history" | "logs"; response?: GatewayResponse; send: Send; disabled: boolean }) {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(5);
  const [level, setLevel] = useState("");
  const [query, setQuery] = useState<Data>({});
  const isHistory = kind === "history";
  const source = response?.data?.[isHistory ? "records" : "entries"];
  const rows = (Array.isArray(source) ? source.map(object) : []).filter(row => !level || display(row.level).toUpperCase() === level);
  const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1));
  const op = isHistory ? "get_history" : "get_logs";
  function request(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    const selectedDate = String(fields.get("date") ?? "");
    const next: Data = { cursor: 0, limit: isHistory ? 8 : 6 };
    if (isHistory) next.sensor = fields.get("sensor") || "ALL";
    if (selectedDate) { next.from_utc = Math.floor(new Date(`${selectedDate}T00:00:00`).getTime() / 1000); next.to_utc = Math.floor(new Date(`${selectedDate}T23:59:59`).getTime() / 1000); }
    setQuery(next); setPage(0); void send(op, next);
  }
  return <Panel id={isHistory ? "gateway-history" : "gateway-logs"} icon={isHistory ? Signal : FileText} title={isHistory ? "History & Diagnostics" : "Gateway Logs"}>
    <form className={styles.filters} onSubmit={request}><label>Date Range<input name="date" type="date" /></label>{isHistory ? <label>Sensor<select name="sensor"><option value="ALL">All sensors</option><option>BSWA308</option><option>WBGT2010SD</option><option>VM6380</option></select></label> : <label>Level<select value={level} onChange={event => { setLevel(event.target.value); setPage(0); }}><option value="">All</option><option>INFO</option><option>WARN</option><option>ERROR</option><option>DEBUG</option></select></label>}<button disabled={disabled} type="submit" className={styles.apply}>Apply</button></form>
    <div className={styles.tableWrap}><table><thead><tr>{(isHistory ? ["Time", "Sensor", "Reading", "Sequence"] : ["Time", "Level", "Message"]).map(label => <th key={label}>{label}</th>)}</tr></thead><tbody>{rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize).map((row, index) => <tr key={index}><td>{date(row.timestamp_utc ?? row.timestamp ?? row.time)}</td>{isHistory ? <><td>{display(row.sensor ?? row.sensor_id)}</td><td>{display(row.values ?? row.reading)}</td><td>{display(row.sequence)}</td></> : <><td><span className={styles.logLevel}>{display(row.level)}</span></td><td>{display(row.message ?? row.text ?? row.detail ?? row)}</td></>}</tr>)}{rows.length === 0 && <tr><td colSpan={isHistory ? 4 : 3} className={styles.empty}>{response ? "No matching records in the received page." : `Request ${isHistory ? "sensor history" : "gateway logs"} to load records.`}</td></tr>}</tbody></table></div>
    <div className={styles.pagination}><label>Rows per page<select value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(0); }}><option>5</option><option>10</option><option>20</option></select></label><span>{rows.length ? `${currentPage * pageSize + 1}–${Math.min((currentPage + 1) * pageSize, rows.length)} of ${rows.length}` : "0 records"}</span><button aria-label="Previous rows" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>‹</button><button aria-label="Next rows" disabled={(currentPage + 1) * pageSize >= rows.length} onClick={() => setPage(currentPage + 1)}>›</button></div>
    {response?.data?.next_cursor != null && response.data.done !== true && <button className={styles.fetchMore} disabled={disabled} onClick={() => void send(op, { limit: isHistory ? 8 : 6, ...(isHistory ? { sensor: "ALL" } : {}), ...query, cursor: response.data?.next_cursor, ...(response.data?.snapshot_id ? { snapshot_id: response.data.snapshot_id } : {}) })}>Retrieve next gateway page</button>}
  </Panel>;
}
