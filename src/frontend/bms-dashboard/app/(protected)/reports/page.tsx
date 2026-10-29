"use client";

import { useState } from "react";
import { Download, FileText } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { DevicesApi, TelemetryApi } from "@/lib/api/resources";
import { API_URL } from "@/lib/api/client";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import type { DeviceDto } from "@/types/api";

async function downloadCsv(path: string, filename: string) {
  const token = window.localStorage.getItem("bms.accessToken");
  const response = await fetch(`${API_URL}${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!response.ok) {
    alert("Report generation failed");
    return;
  }
  const blob = await response.blob();
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  window.URL.revokeObjectURL(url);
}

function RollupSection({ devices }: { devices: DeviceDto[] }) {
  const [deviceId, setDeviceId] = useState("");
  const [granularity, setGranularity] = useState<"Daily" | "Weekly">("Daily");
  const activeId = deviceId || devices[0]?.id || "";

  const query = useApi(() => (activeId ? TelemetryApi.rollup(activeId, granularity) : Promise.resolve([])), [activeId, granularity]);

  // A key that is calculated for some periods is one column, as on the
  // device page: the measured figures where the device sent the value.
  const derivedSeries = Array.from(new Set(query.data?.flatMap((p) => Object.keys(p.derived ?? {})) ?? [])).sort();
  const calculated = new Set(derivedSeries.map((k) => k.toLowerCase()));
  const series = Array.from(new Set(query.data?.flatMap((p) => Object.keys(p.fields)) ?? []))
    .filter((k) => !calculated.has(k.toLowerCase()))
    .sort();
  // A quantity (consumption) is reported as a period total, a level as min / avg / max.
  const isTotal = (key: string) => query.data?.some((p) => p.derived?.[key]?.total != null) ?? false;

  return (
    <div className="bg-card border border-border rounded-lg p-4 space-y-3 shadow-sm">
      <div>
        <h2 className="text-sm font-semibold text-foreground">Aggregated telemetry (min / avg / max)</h2>
        <p className="text-xs text-muted-foreground mt-0.5">
          {granularity === "Daily" ? "Last 30 days, one row per day." : "Last 12 weeks, one row per week."}
        </p>
      </div>
      <div className="flex flex-wrap gap-2 items-end">
        <select value={activeId} onChange={(e) => setDeviceId(e.target.value)} className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5">
          {devices.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </select>
        <div className="flex gap-1">
          {(["Daily", "Weekly"] as const).map((g) => (
            <button
              key={g}
              onClick={() => setGranularity(g)}
              className={`text-xs px-2 py-1.5 rounded ${granularity === g ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground hover:bg-muted"}`}
            >
              {g}
            </button>
          ))}
        </div>
      </div>

      {query.loading && <LoadingState />}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!query.data || query.data.length === 0) && (
        <EmptyState message="No telemetry in this range yet." />
      )}

      {!query.loading && !query.error && query.data && query.data.length > 0 && (
        <div className="max-h-[420px] overflow-y-auto border border-border rounded-lg">
          <table className="w-full text-sm">
            <thead className="bg-muted text-muted-foreground text-xs uppercase tracking-wide font-medium sticky top-0">
              <tr className="divide-x divide-border">
                <th className="text-left px-3 py-2">Period</th>
                {series.map((key) => (
                  <th key={key} className="text-left px-3 py-2">
                    {key} (min / avg / max)
                  </th>
                ))}
                {derivedSeries.map((key) => (
                  <th key={`derived-${key}`} className="text-left px-3 py-2 text-primary" title="Calculated from measured telemetry (see Calculations)">
                    ƒ {key} ({isTotal(key) ? "total" : "min / avg / max"})
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {[...query.data].reverse().map((p) => (
                <tr key={p.periodStart} className="divide-x divide-border">
                  <td className="px-3 py-1.5 text-muted-foreground whitespace-nowrap">{new Date(p.periodStart).toLocaleDateString()}</td>
                  {series.map((key) => {
                    const stats = p.fields[key];
                    return (
                      <td key={key} className="px-3 py-1.5 text-foreground tabular-nums">
                        {stats ? `${stats.min.toFixed(2)} / ${stats.mean.toFixed(2)} / ${stats.max.toFixed(2)}` : "—"}
                      </td>
                    );
                  })}
                  {derivedSeries.map((key) => {
                    const measured = p.fields[key];
                    const calc = p.derived?.[key];
                    let text = "—";
                    if (measured) text = `${measured.min.toFixed(2)} / ${measured.mean.toFixed(2)} / ${measured.max.toFixed(2)}`;
                    else if (calc?.total != null) text = calc.total.toFixed(2);
                    else if (calc?.mean != null) text = `${(calc.min ?? calc.mean).toFixed(2)} / ${calc.mean.toFixed(2)} / ${(calc.max ?? calc.mean).toFixed(2)}`;
                    return (
                      <td
                        key={`derived-${key}`}
                        className={`px-3 py-1.5 tabular-nums bg-primary/5 ${measured ? "text-foreground" : "text-primary"}`}
                        title={measured ? "Measured by the device in this period" : undefined}
                      >
                        {text}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default function ReportsPage() {
  const { effectiveTenantId } = useTenantScope();
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);
  const [deviceId, setDeviceId] = useState("");
  const [range, setRange] = useState("TwentyFourHours");

  if (devicesQuery.loading) return <LoadingState />;
  if (devicesQuery.error) return <ErrorState message={devicesQuery.error} />;

  const activeDeviceId = deviceId || devicesQuery.data?.items[0]?.id || "";

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2">
        <FileText className="h-5 w-5 text-muted-foreground" />
        <div>
          <h1 className="text-xl font-semibold text-foreground">Reports</h1>
          <p className="text-sm text-muted-foreground">Export telemetry and alert history as CSV.</p>
        </div>
      </div>

      <div className="bg-card border border-border rounded-lg p-4 space-y-3 shadow-sm">
        <h2 className="text-sm font-semibold text-foreground">Telemetry report (CSV)</h2>
        <div className="flex flex-wrap gap-2 items-end">
          <select
            value={activeDeviceId}
            onChange={(e) => setDeviceId(e.target.value)}
            className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5"
          >
            {devicesQuery.data?.items.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
          <select
            value={range}
            onChange={(e) => setRange(e.target.value)}
            className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5"
          >
            <option value="OneHour">1 Hour</option>
            <option value="SixHours">6 Hours</option>
            <option value="TwentyFourHours">24 Hours</option>
            <option value="SevenDays">7 Days</option>
          </select>
          <button
            onClick={() => downloadCsv(`/api/reports/telemetry?deviceId=${activeDeviceId}&range=${range}`, `telemetry-${activeDeviceId}.csv`)}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
          >
            <Download className="h-4 w-4" /> Download CSV
          </button>
        </div>
      </div>

      <div className="bg-card border border-border rounded-lg p-4 space-y-3 shadow-sm">
        <h2 className="text-sm font-semibold text-foreground">Alerts report (CSV)</h2>
        <button
          onClick={() => downloadCsv(`/api/reports/alerts${effectiveTenantId ? `?tenantId=${effectiveTenantId}` : ""}`, "alerts-report.csv")}
          className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
        >
          <Download className="h-4 w-4" /> Download CSV
        </button>
      </div>

      {devicesQuery.data && devicesQuery.data.items.length > 0 && <RollupSection devices={devicesQuery.data.items} />}

      <p className="text-xs text-muted-foreground">
        CSV export covers telemetry and alert history; Excel/PDF formats are a documented follow-up (see docs/troubleshooting/common-issues.md) — the CSV opens directly in Excel in the meantime.
      </p>
    </div>
  );
}
