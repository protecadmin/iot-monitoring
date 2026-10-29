"use client";

import { useState } from "react";
import { BellRing } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi } from "@/lib/api/resources";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { GlobalRulesManager } from "@/components/alerts/GlobalRulesManager";

/**
 * Alert rules for every company, by device type. A rule created here applies
 * to all companies' devices of that type — including companies and devices
 * added later; each company can change a value for itself on its Alerts page.
 */
export default function AlertRulesPage() {
  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  const [picked, setPicked] = useState("");
  const deviceTypeId = picked || typesQuery.data?.[0]?.id || "";
  const selected = typesQuery.data?.find((t) => t.id === deviceTypeId);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-2">
          <BellRing className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">Alert Rules</h1>
            <p className="text-xs text-muted-foreground mt-0.5">
              Defined once per device type and applied to every company — new companies and devices are covered automatically. A company can change
              a value for itself on its Alerts page.
            </p>
          </div>
        </div>
        {typesQuery.data && typesQuery.data.length > 0 && (
          <select
            value={deviceTypeId}
            onChange={(e) => setPicked(e.target.value)}
            className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5 min-w-56"
            aria-label="Device type"
          >
            {typesQuery.data.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {typesQuery.loading && <LoadingState />}
      {typesQuery.error && <ErrorState message={typesQuery.error} />}
      {typesQuery.data?.length === 0 && <EmptyState message="No device types yet." hint="Create a device type first — alert rules are defined per type." icon={BellRing} />}
      {selected && (
        <>
          <p className="text-xs text-muted-foreground">
            Rules for <span className="font-medium text-foreground">{selected.name}</span>, then the ones every device type shares (offline, no telemetry).
          </p>
          <GlobalRulesManager key={selected.id} deviceTypeId={selected.id} />
        </>
      )}
    </div>
  );
}
