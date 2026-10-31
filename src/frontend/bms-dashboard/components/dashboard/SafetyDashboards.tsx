"use client";

import { useState } from "react";
import Link from "next/link";
import { Activity, ArrowUpRight, AudioLines, Radio, ThermometerSun, type LucideIcon } from "lucide-react";
import type { WidgetItem } from "./useWidgetData";
import { formatValue, isOffline, LEVEL_ORDER, LEVEL_STYLE, LimitBar } from "./widgets/shared";

const MONITORS = [
  { key: "noiseLevel", title: "Noise", subtitle: "Sound level monitoring", unit: "dB(A)", icon: AudioLines, accent: "text-violet-600 dark:text-violet-400", surface: "bg-violet-500/10", border: "border-t-violet-500", color: "#8b5cf6" },
  { key: "vibration", title: "Vibration", subtitle: "Machine condition monitoring", unit: "mm/s", icon: Activity, accent: "text-cyan-600 dark:text-cyan-400", surface: "bg-cyan-500/10", border: "border-t-cyan-500", color: "#06b6d4" },
  { key: "wbgt", title: "WBGT", subtitle: "Heat stress monitoring", unit: "°C", icon: ThermometerSun, accent: "text-orange-600 dark:text-orange-400", surface: "bg-orange-500/10", border: "border-t-orange-500", color: "#f97316" },
] as const;

type Monitor = { key: string; title: string; subtitle: string; unit: string; icon: LucideIcon; accent: string; surface: string; border: string; color: string };

export function isFocusedSafetyItem(item: WidgetItem) {
  return item.kind === "SafetyStatus" && MONITORS.some((monitor) => monitor.key.toLowerCase() === item.parameter.key.toLowerCase());
}

/** Three focused monitors sharing the existing tenant/site data and live connection. */
export function SafetyDashboards({ items, loading }: { items: WidgetItem[]; loading: boolean }) {
  return (
    <section className="space-y-3" aria-label="Noise, vibration and heat stress dashboards">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-muted-foreground">Monitoring overview</p>
          <h2 className="mt-1 text-lg font-semibold tracking-tight">Environment & machine health</h2>
        </div>
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground"><Radio className="h-3.5 w-3.5" /> Live readings · configured alert limits</span>
      </div>
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        {MONITORS.map((monitor) => <MonitorPanel key={monitor.key} monitor={monitor} items={items} loading={loading} />)}
      </div>
    </section>
  );
}

function MonitorPanel({ monitor, items, loading }: { monitor: Monitor; items: WidgetItem[]; loading: boolean }) {
  const [selectedId, setSelectedId] = useState("");
  const devices = items.filter((item) => isFocusedSafetyItem(item) && item.parameter.key.toLowerCase() === monitor.key.toLowerCase()).sort(
    (a, b) => Number(isOffline(a.device)) - Number(isOffline(b.device)) || LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level] || a.device.name.localeCompare(b.device.name),
  );
  const selected = devices.find((item) => item.device.id === selectedId) ?? devices[0];
  const offline = selected ? isOffline(selected.device) : false;
  const value = !offline && Number.isFinite(selected?.value) ? selected?.value : undefined;
  const status = offline ? "Offline" : value === undefined ? "No data" : selected?.level === "unknown" ? "No limits" : LEVEL_STYLE[selected!.level].label;
  const style = LEVEL_STYLE[offline || value === undefined ? "unknown" : selected!.level];
  const reporting = devices.filter((item) => !isOffline(item.device) && Number.isFinite(item.value));
  const attention = reporting.filter((item) => item.level === "critical" || item.level === "warning").length;
  const samples = offline ? [] : (selected?.trend ?? []).filter(Number.isFinite);
  const companions = selected ? items.filter((item) => item.device.id === selected.device.id && item.parameter.key !== selected.parameter.key).slice(0, 3) : [];
  const high = selected?.limits?.high;
  const scale = high && Number.isFinite(high.critical) && high.critical > 0 ? high.critical * 1.2 : high && Number.isFinite(high.warning) && high.warning > 0 ? high.warning * 1.5 : undefined;
  const fill = value !== undefined && scale ? Math.max(0, Math.min(100, value / scale * 100)) : 0;
  const Icon = monitor.icon;

  return (
    <article className={`min-w-0 overflow-hidden rounded-xl border border-border border-t-[3px] bg-card shadow-sm ${monitor.border}`}>
      <div className="flex items-center justify-between gap-2 px-5 pt-5">
        <div className="flex items-center gap-3">
          <div className={`rounded-xl p-2.5 ${monitor.surface} ${monitor.accent}`}><Icon className="h-5 w-5" /></div>
          <div><h3 className="text-base font-semibold">{monitor.title}</h3><p className="text-[11px] text-muted-foreground">{monitor.subtitle}</p></div>
        </div>
        <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium ${style.badge}`}>
          <span className="h-1.5 w-1.5 rounded-full bg-current" />{loading ? "Loading" : status}
        </span>
      </div>

      <div className="px-5 pb-4 pt-4">
        <label className="sr-only" htmlFor={`monitor-${monitor.key}`}>{monitor.title} device</label>
        <select id={`monitor-${monitor.key}`} value={selected?.device.id ?? ""} onChange={(event) => setSelectedId(event.target.value)} disabled={!devices.length} className="w-full truncate rounded-lg border border-input bg-background px-3 py-2 text-xs">
          {!devices.length && <option value="">{loading ? "Loading devices…" : "No matching devices in this scope"}</option>}
          {devices.map((item) => <option key={item.device.id} value={item.device.id}>{item.device.name}{isOffline(item.device) ? " · offline" : ""}</option>)}
        </select>

        <div className="relative mx-auto my-3 h-44 w-44">
          <svg viewBox="0 0 160 160" className="h-full w-full -rotate-90" aria-hidden="true">
            <circle cx="80" cy="80" r="69" fill="none" stroke="var(--muted)" strokeWidth="8" />
            <circle cx="80" cy="80" r="69" fill="none" stroke={fill === 0 ? "transparent" : selected?.level === "critical" || selected?.level === "warning" ? style.color : monitor.color} strokeWidth="8" pathLength="100" strokeDasharray={`${fill} 100`} strokeLinecap="round" className="transition-[stroke-dasharray] duration-500 motion-reduce:transition-none" />
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-[10px] uppercase tracking-widest text-muted-foreground">{loading ? "Loading" : offline ? "Last reading unavailable" : "Current reading"}</span>
            <span className="mt-1 text-4xl font-semibold tracking-tight tabular-nums">{formatValue(value)}</span>
            <span className={`mt-1 text-xs font-medium ${monitor.accent}`}>{selected?.parameter.unit || monitor.unit}</span>
          </div>
        </div>

        <div className="min-h-12">
          {selected?.limits ? <>
            <div className="mb-2 flex justify-between text-[10px] text-muted-foreground"><span>Configured operating limits</span><span>{selected.parameter.unit || monitor.unit}</span></div>
            <LimitBar value={value} range={selected.limits} />
          </> : <p className="py-3 text-center text-xs text-muted-foreground">{loading ? "Loading monitor…" : !selected ? "Assign a sensor to monitor this area." : value === undefined ? "Waiting for the first reading." : "No alert limits configured."}</p>}
        </div>

        <div className="mt-3 grid grid-cols-2 gap-2">
          <Metric label="Recent minimum" value={formatValue(samples.length ? Math.min(...samples) : undefined)} unit={selected?.parameter.unit || monitor.unit} />
          <Metric label="Recent maximum" value={formatValue(samples.length ? Math.max(...samples) : undefined)} unit={selected?.parameter.unit || monitor.unit} />
        </div>
        <p className="mt-1.5 text-[10px] text-muted-foreground">{samples.length ? `${samples.length} recent samples` : "No recent samples available"}</p>

        <div className="mt-3 min-h-14 rounded-lg bg-muted/40 px-3 py-2">
          {companions.length ? <div className="flex flex-wrap justify-between gap-3">{companions.map((item) => <div key={item.parameter.key} className="min-w-0"><p className="text-[10px] text-muted-foreground">{item.parameter.label}</p><p className="mt-0.5 text-sm font-medium tabular-nums">{formatValue(offline ? undefined : item.value)} <span className="text-[10px] font-normal text-muted-foreground">{item.parameter.unit}</span></p></div>)}</div> : <p className="text-xs leading-5 text-muted-foreground">{offline ? "Device offline. Reconnect it to resume live monitoring." : value === undefined ? "Live measurements will appear when the sensor reports." : selected?.level === "unknown" ? "Configure alert limits to evaluate this reading." : selected?.level === "normal" ? "Current reading is within the configured limits." : "Reading outside configured limits. Review device alerts."}</p>}
        </div>
      </div>

      <footer className="flex flex-wrap items-center justify-between gap-2 border-t border-border bg-muted/20 px-5 py-3">
        <span className="text-[11px] text-muted-foreground">{reporting.length}/{devices.length} reporting{attention > 0 && <span className="ml-2 font-medium text-amber-700 dark:text-amber-400">· {attention} need attention</span>}</span>
        {selected && <Link href={`/devices/${selected.device.id}`} className={`flex items-center gap-1 text-[11px] font-semibold ${monitor.accent}`} aria-label={`Inspect ${selected.device.name}`}>Inspect device <ArrowUpRight className="h-3.5 w-3.5" /></Link>}
      </footer>
    </article>
  );
}

function Metric({ label, value, unit }: { label: string; value: string; unit: string }) {
  return <div className="rounded-lg border border-border px-3 py-2"><p className="text-[10px] text-muted-foreground">{label}</p><p className="mt-1 text-base font-semibold tabular-nums">{value} <span className="text-[10px] font-normal text-muted-foreground">{unit}</span></p></div>;
}
