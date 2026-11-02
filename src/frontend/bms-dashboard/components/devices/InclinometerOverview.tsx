"use client";

import { useEffect, useState } from "react";
import { Activity, BatteryMedium, ChevronDown, Clock3, Info, Move3D, RefreshCw, Thermometer } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { TelemetryApi } from "@/lib/api/resources";
import { formatMeasurement, formatTiltNumber } from "@/lib/format";
import { BaselinePanel } from "@/components/devices/BaselinePanel";
import { DailyTiltTab } from "@/components/devices/tabs/DailyTiltTab";
import type { DeviceDto, DeviceTypeParameterDto, TelemetryPointDto } from "@/types/api";
import type { TiltAxes } from "@/types/tilt";

const AXIS_STYLES = {
  x: { ink: "text-primary", soft: "bg-primary/8", color: "var(--primary)" },
  y: { ink: "text-info", soft: "bg-info/8", color: "var(--info)" },
  z: { ink: "text-accent", soft: "bg-accent/8", color: "var(--accent)" },
};
const AXES = ["x", "y", "z"] as const;

function finite(value: number | undefined): number | null {
  return value !== undefined && Number.isFinite(value) ? value : null;
}

function AngleGauge({ axis, value, label, configured }: { axis: typeof AXES[number]; value: number | null; label: string; configured: boolean }) {
  const style = AXIS_STYLES[axis];
  // Geometry only. The number keeps its full value even beyond the display scale.
  const radians = Math.max(-90, Math.min(90, value ?? 0)) * Math.PI / 180;
  const outsideScale = value !== null && Math.abs(value) > 90;
  return <article data-axis={axis} className="relative overflow-hidden rounded-2xl border border-border bg-card p-5 shadow-sm">
    <div className="flex items-center justify-between gap-3">
      <div className="flex items-center gap-3"><span className={`flex h-10 w-10 items-center justify-center rounded-xl text-sm font-bold ${style.soft} ${style.ink}`}>{axis.toUpperCase()}</span><div><h3 className="text-sm font-semibold">{label}</h3><p className="mt-0.5 text-xs text-muted-foreground">Current angle</p></div></div>
      <Move3D className={`h-4 w-4 ${style.ink}`} aria-hidden="true" />
    </div>
    <svg viewBox="0 0 280 148" className="mx-auto mt-3 h-[138px] w-full max-w-[310px]" role="img" aria-label={`${label}: ${value === null ? "No reading" : `${value.toFixed(4)} degrees`}. Display scale minus 90 to plus 90 degrees.`}>
      <path d="M 34 112 A 106 106 0 0 1 246 112" fill="none" stroke="var(--muted)" strokeWidth="12" strokeLinecap="round" />
      <path d="M 34 112 A 106 106 0 0 1 246 112" fill="none" stroke={style.color} strokeOpacity="0.22" strokeWidth="12" strokeLinecap="round" />
      {[-90,-60,-30,0,30,60,90].map(deg => { const a=deg*Math.PI/180;return <line key={deg} x1={140+Math.sin(a)*89} y1={112-Math.cos(a)*89} x2={140+Math.sin(a)*95} y2={112-Math.cos(a)*95} stroke="var(--muted-foreground)" strokeOpacity="0.5" strokeWidth={deg===0?2:1} />; })}
      <line x1="140" y1="112" x2="140" y2="43" stroke={style.color} strokeWidth="3" strokeLinecap="round" opacity="0.2" strokeDasharray="3 5" />
      {value !== null && <g data-gauge-needle="true"><line x1="140" y1="112" x2={140+Math.sin(radians)*77} y2={112-Math.cos(radians)*77} stroke={style.color} strokeWidth="4" strokeLinecap="round" /><circle cx="140" cy="112" r="8" fill="var(--card)" stroke={style.color} strokeWidth="3" /></g>}
      <g fill="var(--muted-foreground)" fontSize="10" fontFamily="inherit"><text x="32" y="139" textAnchor="middle">−90°</text><text x="140" y="26" textAnchor="middle">0°</text><text x="248" y="139" textAnchor="middle">+90°</text></g>
    </svg>
    <div className="text-center"><p className={`font-mono text-[30px] font-semibold leading-tight tracking-tight tabular-nums ${value===null?"text-muted-foreground":"text-foreground"}`}>{formatTiltNumber(value,true)}{value!==null&&<span className="ml-1 text-xl font-normal text-muted-foreground">°</span>}</p>
      <p className="mt-2 text-[11px] text-muted-foreground">{!configured?"Axis not available on this sensor":value===null?"Waiting for a sensor reading":outsideScale?"Value exceeds the ±90° display scale":"Angle measured by the sensor"}</p>
    </div>
  </article>;
}

export function InclinometerOverview({ device, parameters, axes }: { device: DeviceDto; parameters: DeviceTypeParameterDto[]; axes: TiltAxes }) {
  const query = useApi(() => TelemetryApi.latestForDevice(device.id), [device.id]);
  const [pushed, setPushed] = useState<TelemetryPointDto | null>(null);
  useRealtime({ TelemetryUpdated: payload => { const p=payload as TelemetryPointDto & {deviceId:string};if(p.deviceId===device.id)setPushed(previous=>!previous||new Date(p.timestamp)>=new Date(previous.timestamp)?p:previous); } });
  const refetch = query.refetch;
  useEffect(()=>{const timer=window.setInterval(refetch,30000);return()=>window.clearInterval(timer);},[refetch]);
  const fetched = query.data?.point;
  const point = pushed && (!fetched || new Date(pushed.timestamp)>=new Date(fetched.timestamp)) ? pushed : fetched;
  const fields = point?.fields ?? {};
  const measured = parameters.filter(p=>p.kind!=="Derived");
  const acceleration = measured.filter(p=>p.unit?.trim()==="g").sort((a,b)=>a.label.localeCompare(b.label));
  const temperature = measured.find(p=>p.unit?.replace(/\s/g,"")==="°C");
  const battery = measured.find(p=>p.key==="batteryLevel"||p.unit?.replace(/\s/g,"")==="0–9");
  const temperatureValue = temperature ? finite(fields[temperature.key]) : null;
  const batteryValue = battery ? finite(fields[battery.key]) : null;
  const shownBattery = batteryValue===null?0:Math.max(0,Math.min(9,Math.round(batteryValue)));
  const timestamp = point?.timestamp;
  const axisLabel = (key: string | null, axis: string) => parameters.find(p=>p.key===key)?.label ?? `${axis.toUpperCase()}-axis inclination`;

  return <div className="space-y-5" data-inclinometer-overview="true">
    <section aria-labelledby="inclination-title" className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-gradient-to-r from-secondary/70 via-card to-card px-5 py-4">
        <div className="flex items-center gap-3"><span className="flex h-11 w-11 items-center justify-center rounded-xl border border-primary/15 bg-card text-primary"><Move3D className="h-5 w-5" /></span><div><p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted-foreground">Sensor overview</p><h2 id="inclination-title" className="text-base font-semibold tracking-tight">{axes.z?"Three-axis inclination":"Inclination monitoring"}</h2></div></div>
        <div className="flex items-center gap-4"><div className="text-right"><p className="flex items-center justify-end gap-1.5 text-[11px] text-muted-foreground"><Clock3 className="h-3 w-3" />{device.timeSource==="Received"?"Last received":"Last measurement"}</p><p className="mt-1 text-xs font-medium">{timestamp?new Date(timestamp).toLocaleString():query.loading?"Loading readings…":"Awaiting data"}</p></div><button type="button" aria-label="Refresh sensor readings" title="Refresh sensor readings" onClick={refetch} disabled={query.loading} className="rounded-xl border border-border bg-card p-2.5 text-primary transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-50"><RefreshCw className={`h-4 w-4 ${query.loading?"animate-spin":""}`} /></button></div>
      </div>
      {query.error && <p role="alert" className="rounded-lg border border-destructive/20 bg-destructive/5 p-3 text-sm text-destructive">{query.error}. Displaying the last available reading.</p>}
      <div className="grid gap-4 md:grid-cols-3">{AXES.map(axis=><AngleGauge key={axis} axis={axis} value={axes[axis]?finite(fields[axes[axis]!]):null} label={axisLabel(axes[axis],axis)} configured={!!axes[axis]} />)}</div>
    </section>

    <section aria-label="Supporting sensor readings" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <article className="rounded-2xl border border-border bg-card p-5 shadow-sm sm:col-span-2"><div className="mb-4 flex items-center justify-between"><h3 className="flex items-center gap-2 text-sm font-semibold"><Activity className="h-4 w-4 text-info" />Acceleration</h3><span className="rounded-md bg-muted px-2 py-1 text-[10px] text-muted-foreground">g · gravity</span></div>
        <div className="grid grid-cols-3 gap-3">{acceleration.length?acceleration.map((parameter,index)=><div key={parameter.key} className={`${index?"border-l border-border pl-3":""}`}><p className="text-[11px] text-muted-foreground">{parameter.label}</p><p className="mt-2 font-mono text-lg font-semibold tabular-nums">{finite(fields[parameter.key])===null?"—":formatMeasurement(fields[parameter.key],parameter.key)}</p></div>):<p className="col-span-3 text-xs text-muted-foreground">No acceleration parameters configured.</p>}</div>
      </article>
      <article className="rounded-2xl border border-border bg-card p-5 shadow-sm"><div className="flex items-center justify-between"><h3 className="text-sm font-semibold">Temperature</h3><Thermometer className="h-4 w-4 text-accent" /></div><p className="mt-4 font-mono text-2xl font-semibold tabular-nums">{temperatureValue===null?"—":temperatureValue.toFixed(2)}<span className="ml-1.5 font-sans text-sm font-normal text-muted-foreground">°C</span></p><p className="mt-2 text-[11px] text-muted-foreground">Sensor temperature</p></article>
      <article className="rounded-2xl border border-border bg-card p-5 shadow-sm"><div className="flex items-center justify-between"><h3 className="text-sm font-semibold">Battery</h3><BatteryMedium className="h-4 w-4 text-primary" /></div><p className="mt-3 font-mono text-2xl font-semibold tabular-nums">{batteryValue===null?"—":String(batteryValue)}<span className="ml-1 font-sans text-sm font-normal text-muted-foreground">/ 9</span></p><div className="mt-2 flex gap-1" role="img" aria-label={batteryValue===null?"Battery reading unavailable":`Battery level ${batteryValue} out of 9`}>{Array.from({length:9},(_,i)=><span key={i} className={`h-2 flex-1 rounded-sm ${i<shownBattery?"bg-primary/75":"bg-muted"}`} />)}</div><p className="mt-2 text-[11px] text-muted-foreground">Device battery scale</p></article>
    </section>

    <DailyTiltTab deviceId={device.id} />

    <details className="group rounded-2xl border border-border bg-card shadow-sm"><summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-5 py-4 text-sm font-semibold"><span className="flex items-center gap-2"><Info className="h-4 w-4 text-muted-foreground" />Device details<span className="hidden text-xs font-normal text-muted-foreground sm:inline">· {device.deviceCode}</span></span><ChevronDown className="h-4 w-4 text-muted-foreground transition-transform group-open:rotate-180" /></summary><dl className="grid grid-cols-2 gap-5 border-t border-border px-5 py-5 text-xs md:grid-cols-4">{[["Device code",device.deviceCode],["Serial number",device.serialNumber],["Manufacturer",device.manufacturer],["Model",device.model],["Connection",device.connection==="Connector"?"TCP connector":device.connection],["Sensor ID",device.sensorId],["Firmware",device.firmwareVersion],["Hardware",device.hardwareVersion]].map(([label,value])=><div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="mt-1.5 break-words font-medium">{value||"—"}</dd></div>)}</dl></details>
    <BaselinePanel device={device} parameters={parameters} />
  </div>;
}
