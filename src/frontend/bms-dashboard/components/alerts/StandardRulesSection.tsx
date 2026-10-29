"use client";

import { useMemo, useState } from "react";
import { RotateCcw } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi, DevicesApi, GlobalAlertRulesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import type { AlertRuleLevel, EffectiveAlertRuleDto } from "@/types/api";

const SEVERITIES = ["Info", "Warning", "Critical"];

const OPERATOR_SYMBOL: Record<string, string> = {
  GreaterThan: ">",
  LessThan: "<",
  GreaterThanOrEqual: "≥",
  LessThanOrEqual: "≤",
  Equal: "=",
};

/** Where a value comes from, shown next to it — "Global" values are left plain. */
function LevelTag({ level }: { level: AlertRuleLevel }) {
  if (level === "Global") return null;
  return <span className="ml-1.5 inline-block px-1.5 py-0.5 rounded bg-primary/10 text-primary text-[10px] font-medium align-middle">{level}</span>;
}

/**
 * The alert rules one of this company's devices uses — the standard rules
 * for its device type (defined on Rules → Alert Rules) plus the ones every
 * type shares. Edit / Turn off / Reset change a rule for that device only;
 * every other device keeps following the standard values.
 */
export function StandardRulesSection({ tenantId, canEdit }: { tenantId: string; canEdit: boolean }) {
  const [picked, setPicked] = useState("");
  const [editing, setEditing] = useState<EffectiveAlertRuleDto | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId, pageSize: 200 }), [tenantId]);
  const devices = useMemo(() => [...(devicesQuery.data?.items ?? [])].sort((a, b) => a.name.localeCompare(b.name)), [devicesQuery.data]);
  const device = devices.find((d) => d.id === picked) ?? devices[0];
  const deviceId = device?.id ?? "";
  const rulesQuery = useApi(
    () => (deviceId ? GlobalAlertRulesApi.effective({ tenantId, deviceId }) : Promise.resolve(undefined)),
    [tenantId, deviceId],
  );

  const typeName = useMemo(() => new Map((typesQuery.data ?? []).map((t) => [t.id, t.name])), [typesQuery.data]);

  // Changes are saved for the selected device only.
  const level: { siteId?: string; deviceId?: string } = { deviceId };
  const levelName = device?.name ?? "this device";

  async function setEnabled(rule: EffectiveAlertRuleDto, enabled: boolean) {
    setActionError(null);
    try {
      const here = rule.here;
      await GlobalAlertRulesApi.saveOverride(rule.ruleId, tenantId, {
        ...level,
        isEnabled: enabled,
        threshold: here?.threshold ?? null,
        severity: here?.severity ?? null,
        cooldownSeconds: here?.cooldownSeconds ?? null,
      });
      rulesQuery.refetch();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't change the rule");
    }
  }

  async function reset(rule: EffectiveAlertRuleDto) {
    setActionError(null);
    try {
      await GlobalAlertRulesApi.resetOverride(rule.ruleId, tenantId, level);
      rulesQuery.refetch();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't reset the rule");
    }
  }

  const selectClass = "text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5";

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <select value={deviceId} onChange={(e) => setPicked(e.target.value)} className={selectClass} aria-label="Device">
          {devices.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name} · {typeName.get(d.deviceTypeId) ?? "—"}
            </option>
          ))}
        </select>
        {device && <span className="text-xs text-muted-foreground">Changes here apply to {device.name} only.</span>}
      </div>

      {actionError && <p className="text-xs text-destructive">{actionError}</p>}

      {devicesQuery.data && devices.length === 0 ? (
        <EmptyState message="This company has no devices yet." hint="Its devices get the standard rules for their type as soon as they're added." />
      ) : rulesQuery.loading || (!rulesQuery.data && !rulesQuery.error) ? (
        <LoadingState />
      ) : rulesQuery.error ? (
        <ErrorState message={rulesQuery.error} />
      ) : !rulesQuery.data || rulesQuery.data.length === 0 ? (
        <EmptyState message="No standard rules apply here." hint="Global alert rules are defined by a SuperAdmin, per device type." />
      ) : (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Rule</th>
                <th className="text-left px-4 py-2">Device type</th>
                <th className="text-left px-4 py-2">Condition</th>
                <th className="text-left px-4 py-2">Severity</th>
                <th className="text-left px-4 py-2">Cooldown</th>
                <th className="text-left px-4 py-2">Status</th>
                {canEdit && <th className="text-left px-4 py-2">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {rulesQuery.data.map((r) => (
                <tr key={r.ruleId} className={r.isEnabled ? undefined : "opacity-60"}>
                  <td className="px-4 py-2 font-medium">{r.name}</td>
                  <td className="px-4 py-2 text-muted-foreground">{r.deviceTypeId ? (typeName.get(r.deviceTypeId) ?? "—") : "All device types"}</td>
                  <td className="px-4 py-2 text-muted-foreground whitespace-nowrap">
                    <span className="font-mono text-xs">{r.metric}</span> {OPERATOR_SYMBOL[r.operator] ?? r.operator}{" "}
                    <span className="text-foreground tabular-nums">{r.threshold ?? "—"}</span>
                    <LevelTag level={r.thresholdFrom} />
                  </td>
                  <td className="px-4 py-2 whitespace-nowrap">
                    <StatusBadge status={r.severity} />
                    <LevelTag level={r.severityFrom} />
                  </td>
                  <td className="px-4 py-2 text-muted-foreground text-xs whitespace-nowrap">
                    {r.cooldownSeconds}s<LevelTag level={r.cooldownFrom} />
                  </td>
                  <td className="px-4 py-2 text-xs whitespace-nowrap">
                    <span className={r.isEnabled ? "text-emerald-600 dark:text-emerald-400" : "text-muted-foreground"}>{r.isEnabled ? "On" : "Off"}</span>
                    <LevelTag level={r.enabledFrom} />
                  </td>
                  {canEdit && (
                    <td className="px-4 py-2">
                      <div className="flex gap-3 whitespace-nowrap">
                        <button onClick={() => setEditing(r)} className="text-xs text-primary hover:underline">
                          Edit
                        </button>
                        <button onClick={() => setEnabled(r, !r.isEnabled)} className="text-xs text-primary hover:underline">
                          {r.isEnabled ? "Turn off" : "Turn on"}
                        </button>
                        {r.here && (
                          <button onClick={() => reset(r)} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" title={`Remove the change made for ${levelName}`}>
                            <RotateCcw className="h-3 w-3" /> Reset
                          </button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="text-xs text-muted-foreground">
        Values tagged <span className="font-medium">Device</span> were changed for this device (and <span className="font-medium">Company</span> for the
        whole company); untagged ones follow the rule set for every company.
      </p>

      {editing && (
        <OverrideModal
          rule={editing}
          tenantId={tenantId}
          level={level}
          levelName={levelName}
          onClose={() => setEditing(null)}
          onSaved={rulesQuery.refetch}
        />
      )}
    </div>
  );
}

/** Edits the change at one level. Empty fields keep following the level above. */
function OverrideModal({
  rule,
  tenantId,
  level,
  levelName,
  onClose,
  onSaved,
}: {
  rule: EffectiveAlertRuleDto;
  tenantId: string;
  level: { siteId?: string; deviceId?: string };
  levelName: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const here = rule.here;
  const [threshold, setThreshold] = useState(here?.threshold?.toString() ?? "");
  const [severity, setSeverity] = useState(here?.severity ?? "");
  const [cooldown, setCooldown] = useState(here?.cooldownSeconds?.toString() ?? "");
  const [enabled, setEnabled] = useState(here?.isEnabled == null ? "" : here.isEnabled ? "on" : "off");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // What each field falls back to: the value in effect without this level's change.
  const inherited = (from: AlertRuleLevel, value: string) => (here && from !== "Global" ? "the level above" : value);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (threshold && Number.isNaN(Number(threshold))) return setError("Threshold must be a number.");
    if (cooldown && !(Number(cooldown) >= 0)) return setError("Cooldown must be zero or more seconds.");
    setSaving(true);
    try {
      await GlobalAlertRulesApi.saveOverride(rule.ruleId, tenantId, {
        ...level,
        threshold: threshold ? Number(threshold) : null,
        severity: severity || null,
        cooldownSeconds: cooldown ? Number(cooldown) : null,
        isEnabled: enabled === "" ? null : enabled === "on",
      });
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={`${rule.name} — ${levelName}`} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <p className="text-xs text-muted-foreground">
          Leave a field empty to keep following {here ? "the level above" : "the current value"}. Only what you set here is changed, and only for{" "}
          {levelName}.
        </p>
        <div className="grid grid-cols-2 gap-3">
          <FormField label={`Threshold (${rule.metric} ${OPERATOR_SYMBOL[rule.operator] ?? rule.operator} …)`}>
            <input
              type="number"
              step="any"
              value={threshold}
              onChange={(e) => setThreshold(e.target.value)}
              placeholder={inherited(rule.thresholdFrom, String(rule.threshold ?? ""))}
              className={inputClass}
            />
          </FormField>
          <FormField label="Severity">
            <select value={severity} onChange={(e) => setSeverity(e.target.value)} className={inputClass}>
              <option value="">Keep ({rule.severity})</option>
              {SEVERITIES.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Cooldown (seconds)">
            <input
              type="number"
              min={0}
              value={cooldown}
              onChange={(e) => setCooldown(e.target.value)}
              placeholder={inherited(rule.cooldownFrom, String(rule.cooldownSeconds))}
              className={inputClass}
            />
          </FormField>
          <FormField label="Status">
            <select value={enabled} onChange={(e) => setEnabled(e.target.value)} className={inputClass}>
              <option value="">Keep ({rule.isEnabled ? "On" : "Off"})</option>
              <option value="on">On</option>
              <option value="off">Off</option>
            </select>
          </FormField>
        </div>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : "Save change"}
        </button>
      </form>
    </Modal>
  );
}
