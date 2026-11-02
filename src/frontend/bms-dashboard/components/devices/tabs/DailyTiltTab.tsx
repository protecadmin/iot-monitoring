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
  return <div className="space-y-4">
    <section className="rounded-lg border border-border bg-card p-4 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div><h2 className="flex items-center gap-2 text-sm font-semibold"><CalendarDays className="h-4 w-4 text-primary" />Daily tilt comparison</h2>
          <p className="mt-1 text-xs text-muted-foreground">{data.settings ? `Daily cutoff ${data.settings.closingTime} · ${zone}` : "Daily monitoring has not been configured for this device."}</p></div>
        <div className="flex gap-2">
          <button type="button" onClick={query.refetch} disabled={query.loading} className="inline-flex items-center gap-1.5 rounded-md border border-border px-3 py-1.5 text-xs hover:bg-muted disabled:opacity-60"><RefreshCw className="h-3.5 w-3.5" />Refresh</button>
          {canEdit && <button type="button" onClick={() => setEditing(true)} className="inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground"><Settings2 className="h-3.5 w-3.5" />{data.settings ? "Edit daily settings" : "Set up daily readings"}</button>}
        </div>
      </div>
      {data.settings && <div className="mt-4 rounded-md border border-border bg-secondary/30 p-3 text-xs">
        <p className="font-medium">Onboarding reference{data.baseline ? ` · ${dateTime(data.baseline.readingAt)} (${zone})` : " · Waiting for the first complete tilt reading"}</p>
        {data.baseline && <div className="mt-2 flex flex-wrap gap-6 font-mono">{AXES.map(axis => <span key={axis}>{axis.toUpperCase()}: {formatTiltNumber(data.baseline!.values[axis])}°</span>)}</div>}
      </div>}
    </section>

    {data.settings && <section className="overflow-hidden rounded-lg border border-border bg-card shadow-sm">
      <form onSubmit={applyRange} className="flex flex-wrap items-end gap-3 border-b border-border p-4">
        <label className="space-y-1 text-xs text-muted-foreground">From<input type="date" aria-label="Daily readings start date" value={from} onChange={e => setFrom(e.target.value)} className={inputClass} /></label>
        <label className="space-y-1 text-xs text-muted-foreground">To<input type="date" aria-label="Daily readings end date" value={to} onChange={e => setTo(e.target.value)} className={inputClass} /></label>
        <button type="submit" className="rounded-md border border-border px-3 py-2 text-xs hover:bg-muted">Apply dates</button>
        <span className="pb-2 text-xs text-muted-foreground">Defaults to the last 30 days</span>
      </form>
      {(rangeError || query.error) && <p role="alert" className="p-4 text-sm text-destructive">{rangeError || query.error}</p>}
      <div className="overflow-x-auto">
        <table className="w-full min-w-[1100px] text-sm">
          <thead className="bg-muted text-xs text-muted-foreground"><tr>
            <th rowSpan={2} className="px-4 py-3 text-left">Date</th><th rowSpan={2} className="px-4 py-3 text-left">Actual received time</th>
            <th colSpan={3} className="border-l border-border px-3 py-2">Daily reading (°)</th><th colSpan={3} className="border-l border-border px-3 py-2">Previous-day difference (°)</th><th colSpan={3} className="border-l border-border px-3 py-2">Since onboarding (°)</th>
          </tr><tr>{["reading", "previous", "onboarding"].flatMap(group => AXES.map(axis => <th key={`${group}-${axis}`} className="border-l border-border/50 px-3 py-2 text-right">{axis.toUpperCase()}</th>))}</tr></thead>
          <tbody className="divide-y divide-border">
            {data.rows.map(row => <tr key={row.date} className="hover:bg-secondary/30">
              <td className="whitespace-nowrap px-4 py-3">{row.date}</td>
              <td className="whitespace-nowrap px-4 py-3 text-xs text-muted-foreground">{row.readingAt ? dateTime(row.readingAt) : row.status === "NoData" ? "No data" : `Pending · cutoff ${dateTime(row.cutoffAt)}`}</td>
              <ValueCells values={row.reading} /><ValueCells values={row.previousDayDifference} signed /><ValueCells values={row.onboardingDifference} signed />
            </tr>)}
            {data.rows.length === 0 && <tr><td colSpan={11} className="px-4 py-10 text-center text-sm text-muted-foreground">No daily dates in this range after onboarding. The first row will be available at the configured cutoff.</td></tr>}
          </tbody>
        </table>
      </div>
      <p className="border-t border-border p-4 text-xs leading-5 text-muted-foreground">One last complete reading at or before the cutoff. Differences are current minus reference; a missing previous day stays blank. All times use {zone}. {data.axes.z ? "" : "This device has no Z axis; Z stays blank."}</p>
    </section>}
    {editing && <ScheduleEditor deviceId={deviceId} settings={data.settings} onClose={() => setEditing(false)} onSaved={query.refetch} />}
  </div>;
}
