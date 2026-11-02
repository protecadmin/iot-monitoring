"use client";

import { useEffect, useState, type FormEvent } from "react";
import { CalendarDays, RefreshCw, Settings2 } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { useCan } from "@/lib/auth/permissions";
import { TiltApi } from "@/lib/api/tilt";
import { ApiError } from "@/lib/api/client";
import { formatTiltNumber } from "@/lib/format";
import { Modal, inputClass } from "@/components/ui/Modal";
import { ErrorState, LoadingState } from "@/components/ui/States";
import { TiltScheduleFields } from "@/components/devices/TiltScheduleFields";
import type { TiltSettings, TiltValues } from "@/types/tilt";

const AXES = ["x", "y", "z"] as const;
function ValueCells({ values, signed = false }: { values: TiltValues; signed?: boolean }) {
  return AXES.map((axis) => <td key={axis} className="border-l border-border/50 px-3 py-3 text-right font-mono text-xs tabular-nums">{formatTiltNumber(values[axis], signed)}</td>);
}

function ScheduleEditor({ deviceId, settings, onClose, onSaved }: { deviceId: string; settings: TiltSettings | null; onClose: () => void; onSaved: () => void }) {
  const [time, setTime] = useState(settings?.closingTime ?? "23:59");
  const [zone, setZone] = useState(settings?.timeZoneId ?? "Asia/Singapore");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function save(e: FormEvent) {
    e.preventDefault(); setSaving(true); setError(null);
    try { await TiltApi.configure(deviceId, time, zone.trim()); onSaved(); onClose(); }
    catch (err) { setError(err instanceof ApiError ? err.message : "Could not save daily settings"); }
    finally { setSaving(false); }
  }
  return <Modal title="Daily tilt settings" onClose={onClose} size="lg"><form onSubmit={save} className="space-y-4">
    <TiltScheduleFields closingTime={time} timeZoneId={zone} onTimeChange={setTime} onZoneChange={setZone} disabled={saving} />
    {settings && <p className="text-xs text-muted-foreground">Existing daily rows and the onboarding reference stay fixed. Changes apply to days that have not been saved yet.</p>}
    {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    <button disabled={saving} type="submit" className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground disabled:opacity-60">{saving ? "Saving..." : "Save settings"}</button>
  </form></Modal>;
}

export function DailyTiltTab({ deviceId }: { deviceId: string }) {
  const canEdit = useCan()("devices.edit");
  const [editing, setEditing] = useState(false);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [range, setRange] = useState({ from: "", to: "" });
  const [rangeError, setRangeError] = useState<string | null>(null);
  const query = useApi(() => TiltApi.get(deviceId, range.from || undefined, range.to || undefined), [deviceId, range.from, range.to]);
  // A closed daily row or onboarding reference can appear without raw telemetry changing.
  const refetch = query.refetch;
  useEffect(() => { const timer = window.setInterval(refetch, 60000); return () => window.clearInterval(timer); }, [refetch]);
  const data = query.data;
  const zone = data?.settings?.timeZoneId ?? "UTC";
  const dateTime = (value: string) => new Intl.DateTimeFormat(undefined, { timeZone: zone, dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
  const applyRange = (e: FormEvent) => { e.preventDefault(); if (from && to && from > to) { setRangeError("The start date must be before the end date."); return; } setRangeError(null); setRange({ from, to }); };

  if (query.loading && !data) return <LoadingState label="Loading daily tilt readings..." />;
  if (query.error && !data) return <ErrorState message={query.error} />;
  if (!data) return null;
  return <section aria-labelledby="daily-tilt-heading" className="min-w-0 overflow-hidden rounded-2xl border border-border bg-card shadow-sm">
    <div className="flex flex-wrap items-center justify-between gap-4 px-5 py-5">
      <div className="flex items-center gap-3"><span className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/8 text-primary"><CalendarDays className="h-5 w-5" /></span><div><h2 id="daily-tilt-heading" className="text-base font-semibold tracking-tight">Daily tilt comparison</h2><p className="mt-1 text-xs text-muted-foreground">{data.settings ? `One reading per day · Cutoff ${data.settings.closingTime} · ${zone}` : "Choose a daily cutoff to start tracking changes."}</p></div></div>
      <div className="flex flex-wrap gap-2">
        <button type="button" onClick={query.refetch} disabled={query.loading} className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-2 text-xs hover:bg-muted disabled:opacity-60"><RefreshCw className="h-3.5 w-3.5" />Refresh</button>
        {canEdit && <button type="button" onClick={() => setEditing(true)} className="inline-flex items-center gap-1.5 rounded-lg bg-accent px-3 py-2 text-xs font-medium text-accent-foreground hover:bg-accent/90"><Settings2 className="h-3.5 w-3.5" />{data.settings ? "Edit daily settings" : "Set up daily readings"}</button>}
      </div>
    </div>

    {data.settings && <>
      <div className="mx-5 mb-5 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-primary/10 bg-secondary/35 px-4 py-3">
        <div><p className="text-xs font-semibold text-primary">Onboarding reference</p><p className="mt-1 text-[11px] text-muted-foreground">{data.baseline?dateTime(data.baseline.readingAt)+` · ${zone}`:"Waiting for the first complete tilt reading"}</p></div>
        {data.baseline && <div className="flex flex-wrap gap-2">{AXES.map(axis=><span key={axis} className="rounded-lg border border-border/70 bg-card px-3 py-2 font-mono text-xs"><span className="mr-2 font-sans text-[10px] font-semibold text-muted-foreground">{axis.toUpperCase()}</span>{formatTiltNumber(data.baseline!.values[axis])}{data.baseline!.values[axis]!==null?"°":""}</span>)}</div>}
      </div>
      <form onSubmit={applyRange} className="flex flex-wrap items-end gap-3 border-t border-border bg-muted/20 px-5 py-4">
        <label className="space-y-1 text-[11px] font-medium text-muted-foreground">From<input type="date" aria-label="Daily readings start date" value={from} onChange={e => setFrom(e.target.value)} className={inputClass} /></label>
        <label className="space-y-1 text-[11px] font-medium text-muted-foreground">To<input type="date" aria-label="Daily readings end date" value={to} onChange={e => setTo(e.target.value)} className={inputClass} /></label>
        <button type="submit" className="rounded-lg border border-border bg-card px-3 py-2 text-xs font-medium hover:bg-muted">Apply dates</button>
        <span className="pb-2 text-[11px] text-muted-foreground">Last 30 days by default</span>
      </form>
      {(rangeError || query.error) && <p role="alert" className="p-4 text-sm text-destructive">{rangeError || query.error}</p>}
      <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="Daily tilt readings and differences">
        <table className="w-full min-w-[1050px] text-sm">
          <thead className="border-y border-border bg-muted/70 text-[11px] text-muted-foreground"><tr>
            <th rowSpan={2} scope="col" className="px-5 py-3 text-left">Date</th><th rowSpan={2} scope="col" className="px-4 py-3 text-left">Actual received time</th>
            <th colSpan={3} scope="colgroup" className="border-l border-border bg-primary/5 px-3 py-3 text-primary">Daily reading (°)</th><th colSpan={3} scope="colgroup" className="border-l border-border px-3 py-3">Previous-day difference (°)</th><th colSpan={3} scope="colgroup" className="border-l border-border bg-accent/5 px-3 py-3 text-accent-ink">Since onboarding (°)</th>
          </tr><tr>{["reading", "previous", "onboarding"].flatMap(group=>AXES.map(axis=><th key={`${group}-${axis}`} scope="col" className="border-l border-border/50 px-3 pb-2 text-right font-medium">{axis.toUpperCase()}</th>))}</tr></thead>
          <tbody className="divide-y divide-border">
            {data.rows.map(row=><tr key={row.date} className="odd:bg-muted/10 hover:bg-secondary/25">
              <td className="whitespace-nowrap px-5 py-4 text-xs font-medium">{row.date}</td>
              <td className="whitespace-nowrap px-4 py-3 text-[11px] text-muted-foreground">{row.readingAt?dateTime(row.readingAt):<><span className={`inline-flex rounded-md px-2 py-1 text-[10px] font-medium ${row.status==="NoData"?"bg-muted text-muted-foreground":"bg-accent-soft text-accent-ink"}`}>{row.status==="NoData"?"No data":"Pending"}</span>{row.status!=="NoData"&&<span className="mt-1 block">Closes {dateTime(row.cutoffAt)}</span>}</>}</td>
              <ValueCells values={row.reading} /><ValueCells values={row.previousDayDifference} signed /><ValueCells values={row.onboardingDifference} signed />
            </tr>)}
            {data.rows.length===0&&<tr><td colSpan={11} className="px-5 py-10 text-center text-sm text-muted-foreground">No daily dates in this range after onboarding. The first row will be available at the configured cutoff.</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="border-t border-border px-5 py-3 text-[11px] leading-5 text-muted-foreground"><p>Differences = daily reading − reference. A missing previous day stays blank. All daily times use {zone}.</p>{!data.axes.z&&<p>This sensor has no Z axis. Z values stay blank.</p>}{data.axes.z&&data.baseline?.values.z===null&&<p>The onboarding reference has no Z reading; change since onboarding for Z stays blank.</p>}</div>
    </>}
    {editing&&<ScheduleEditor deviceId={deviceId} settings={data.settings} onClose={()=>setEditing(false)} onSaved={query.refetch} />}
  </section>;
}
