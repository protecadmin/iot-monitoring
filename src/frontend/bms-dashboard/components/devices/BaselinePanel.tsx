"use client";

import { useState } from "react";
import { RotateCcw } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { CalculationsApi, DeviceBaselinesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { useCan } from "@/lib/auth/permissions";
import { BASELINE_TYPE } from "@/lib/calculations";
import type { DeviceDto, DeviceTypeParameterDto } from "@/types/api";

/**
 * The device's reference readings, for its "Change from baseline" rules —
 * e.g. a tilt meter's angles at installation, so alerts measure movement.
 * Shown only when such a rule applies to the device. Values are taken from
 * the latest reading, never typed in.
 */
export function BaselinePanel({ device, parameters }: { device: DeviceDto; parameters: DeviceTypeParameterDto[] }) {
  const canEdit = useCan()("devices.edit");
  const rulesQuery = useApi(() => CalculationsApi.eligible(device.tenantId, device.deviceTypeId, device.siteId, device.id), [device.id]);
  const baselinesQuery = useApi(() => DeviceBaselinesApi.list(device.id), [device.id]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Which set is awaiting confirmation: every row ("all") or one parameter's key.
  const [confirming, setConfirming] = useState<string | null>(null);

  // The parameters a baseline rule measures from — what needs a baseline.
  const keys = [...new Set((rulesQuery.data ?? []).filter((e) => e.rule.type === BASELINE_TYPE).map((e) => e.rule.sourceParameterKey))];
  const baselines = baselinesQuery.data ?? [];
  if (keys.length === 0 && baselines.length === 0) return null;

  const label = (key: string) => parameters.find((p) => p.key.toLowerCase() === key.toLowerCase());

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      baselinesQuery.refetch();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't update the baseline");
    } finally {
      setBusy(false);
      setConfirming(null);
    }
  }

  const rows = keys.length > 0 ? keys : baselines.map((b) => b.parameterKey);
  const hasBaseline = (key: string) => baselines.some((b) => b.parameterKey.toLowerCase() === key.toLowerCase());
  // Setting one row leaves the others alone — a parameter added later gets its
  // baseline without restarting the movement the existing ones have recorded.
  const setKeys = (only?: string) => DeviceBaselinesApi.set(device.id, only ? [only] : keys.length > 0 ? keys : undefined);

  return (
    <div className="bg-card border border-border rounded-lg p-4 shadow-sm md:col-span-2">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Baseline</h2>
          <p className="text-xs text-muted-foreground">Change-from-baseline values measure movement from these readings — usually taken right after installation.</p>
        </div>
        {canEdit &&
          (confirming === "all" ? (
            <div className="flex items-center gap-2 text-xs">
              <span className="text-muted-foreground">
                {baselines.length > 0 ? "Replace every baseline below with the latest reading? Movement recorded so far restarts from 0." : "Use the latest reading as the baseline?"}
              </span>
              <button disabled={busy} onClick={() => run(() => setKeys())} className="px-2 py-1 rounded-md bg-primary text-primary-foreground disabled:opacity-50">
                Yes, set
              </button>
              <button onClick={() => setConfirming(null)} className="px-2 py-1 rounded-md border border-border">
                Cancel
              </button>
            </div>
          ) : (
            <button onClick={() => setConfirming("all")} className="text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90">
              {baselines.length > 0 ? (rows.length > 1 ? "Set new baseline for all" : "Set new baseline") : "Set baseline"}
            </button>
          ))}
      </div>

      <table className="w-full text-sm">
        <thead className="text-xs text-muted-foreground">
          <tr>
            <th className="text-left py-1">Parameter</th>
            <th className="text-left py-1">Baseline</th>
            <th className="text-left py-1">From reading at</th>
            {canEdit && <th className="py-1" />}
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {rows.map((key) => {
            const p = label(key);
            const b = baselines.find((x) => x.parameterKey.toLowerCase() === key.toLowerCase());
            return (
              <tr key={key}>
                <td className="py-1.5 text-foreground">{p?.label ?? key}</td>
                <td className="py-1.5 tabular-nums">{b ? `${b.value} ${p?.unit ?? ""}` : <span className="text-amber-600 dark:text-amber-400">Not set — no change values yet</span>}</td>
                <td className="py-1.5 text-muted-foreground text-xs">{b ? new Date(b.readingAt).toLocaleString() : "—"}</td>
                {canEdit && (
                  <td className="py-1.5 text-right whitespace-nowrap">
                    {confirming === key ? (
                      <span className="inline-flex items-center gap-2 text-xs">
                        <span className="text-muted-foreground">{b ? "Replace with the latest reading?" : "Use the latest reading?"}</span>
                        <button disabled={busy} onClick={() => run(() => setKeys(key))} className="px-2 py-0.5 rounded-md bg-primary text-primary-foreground disabled:opacity-50">
                          Yes
                        </button>
                        <button onClick={() => setConfirming(null)} className="px-2 py-0.5 rounded-md border border-border">
                          Cancel
                        </button>
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-3">
                        {/* Per row only when there's more than one — with one row the top button is the same action. */}
                        {rows.length > 1 && (
                          <button disabled={busy} onClick={() => setConfirming(key)} className="text-xs text-primary hover:underline" title={`Set only ${p?.label ?? key}'s baseline; the other rows stay as they are`}>
                            {hasBaseline(key) ? "Set new" : "Set"}
                          </button>
                        )}
                        {b && (
                          <button disabled={busy} onClick={() => run(() => DeviceBaselinesApi.reset(device.id, b.parameterKey))} className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" title="Clear this baseline">
                            <RotateCcw className="h-3 w-3" /> Reset
                          </button>
                        )}
                      </span>
                    )}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
      {error && <p className="text-xs text-destructive mt-2">{error}</p>}
    </div>
  );
}
