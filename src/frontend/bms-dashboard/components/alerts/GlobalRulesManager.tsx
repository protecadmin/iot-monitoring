"use client";

import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi, GlobalAlertRulesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import type { DeviceTypeDto, GlobalAlertRuleDto } from "@/types/api";

const OPERATORS: [string, string][] = [
  ["GreaterThan", ">"],
  ["LessThan", "<"],
  ["GreaterThanOrEqual", "≥"],
  ["LessThanOrEqual", "≤"],
  ["Equal", "="],
];
const SEVERITIES = ["Info", "Warning", "Critical"];

// Not measurements: evaluated from device silence, for every device type.
const LIFECYCLE: [string, string][] = [
  ["DeviceOffline", "Device offline (seconds)"],
  ["NoTelemetry", "No telemetry (seconds)"],
  ["NeverConnected", "Never connected after being added (seconds)"],
];

/**
 * The alert rules defined once for every company (SuperAdmin). Each is for a
 * device type — its metric is one of that type's parameters, calculated ones
 * included — or for every type when it watches device silence.
 */
export function GlobalRulesManager({ deviceTypeId }: { deviceTypeId: string }) {
  const rulesQuery = useApi(() => GlobalAlertRulesApi.list(), []);
  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  const [editing, setEditing] = useState<GlobalAlertRuleDto | "new" | null>(null);
  const [deleting, setDeleting] = useState<GlobalAlertRuleDto | null>(null);

  const typeName = useMemo(() => new Map((typesQuery.data ?? []).map((t) => [t.id, t.name])), [typesQuery.data]);
  // The selected type's own rules first, then the ones for every type
  // (offline / no telemetry), which apply to its devices too.
  const rules = (rulesQuery.data ?? [])
    .filter((r) => r.deviceTypeId === deviceTypeId || r.deviceTypeId === null)
    .sort((a, b) => Number(a.deviceTypeId === null) - Number(b.deviceTypeId === null) || a.name.localeCompare(b.name));

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <button
          onClick={() => setEditing("new")}
          className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
        >
          <Plus className="h-4 w-4" /> New rule for {typeName.get(deviceTypeId) ?? "this type"}
        </button>
      </div>

      {rulesQuery.loading ? (
        <LoadingState />
      ) : rulesQuery.error ? (
        <ErrorState message={rulesQuery.error} />
      ) : rules.length === 0 ? (
        <EmptyState message="No alert rules for this device type yet." hint="Create one — every company's devices of this type get it." />
      ) : (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Rule</th>
                <th className="text-left px-4 py-2">Device type</th>
                <th className="text-left px-4 py-2">Condition</th>
                <th className="text-left px-4 py-2">Severity</th>
                <th className="text-left px-4 py-2">Applies to</th>
                <th className="text-left px-4 py-2">Changes</th>
                <th className="text-left px-4 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {rules.map((r) => (
                <tr key={r.id} className={r.isEnabled ? undefined : "opacity-50"}>
                  <td className="px-4 py-2 font-medium">
                    {r.name}
                    {!r.isEnabled && <span className="ml-1.5 text-xs text-muted-foreground">(off everywhere)</span>}
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{r.deviceTypeId ? (typeName.get(r.deviceTypeId) ?? "—") : "All device types"}</td>
                  <td className="px-4 py-2 text-muted-foreground whitespace-nowrap">
                    <span className="font-mono text-xs">{r.metric}</span> {OPERATORS.find(([o]) => o === r.operator)?.[1] ?? r.operator}{" "}
                    <span className="text-foreground tabular-nums">{r.threshold ?? "—"}</span>
                  </td>
                  <td className="px-4 py-2">
                    <StatusBadge status={r.severity} />
                  </td>
                  <td className="px-4 py-2 text-xs text-muted-foreground">{r.appliesByDefault ? "All companies" : "Selected companies"}</td>
                  <td className="px-4 py-2 text-xs text-muted-foreground" title="Companies, sites and devices that changed this rule">
                    {r.overrideCount || "—"}
                  </td>
                  <td className="px-4 py-2">
                    <div className="flex gap-3">
                      <button onClick={() => setEditing(r)} className="text-xs text-primary hover:underline">
                        Edit
                      </button>
                      <button onClick={() => setDeleting(r)} className="text-xs text-destructive hover:underline">
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {editing && typesQuery.data && (
        <GlobalRuleModal
          rule={editing === "new" ? undefined : editing}
          defaultDeviceTypeId={deviceTypeId}
          deviceTypes={typesQuery.data}
          onClose={() => setEditing(null)}
          onSaved={rulesQuery.refetch}
        />
      )}
      {deleting && (
        <ConfirmModal
          title="Delete global alert rule"
          message={`Delete "${deleting.name}" for every company${deleting.overrideCount ? `, with the ${deleting.overrideCount} change(s) companies made to it` : ""}? Past alerts are kept.`}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await GlobalAlertRulesApi.remove(deleting.id);
            rulesQuery.refetch();
          }}
        />
      )}
    </div>
  );
}

function GlobalRuleModal({
  rule,
  defaultDeviceTypeId,
  deviceTypes,
  onClose,
  onSaved,
}: {
  rule?: GlobalAlertRuleDto;
  /** The type selected on the page — a new rule starts for it. */
  defaultDeviceTypeId: string;
  deviceTypes: DeviceTypeDto[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(rule?.name ?? "");
  const [deviceTypeId, setDeviceTypeId] = useState(rule ? (rule.deviceTypeId ?? "") : defaultDeviceTypeId);
  const [metric, setMetric] = useState(rule?.metric ?? "");
  const [operator, setOperator] = useState(rule?.operator ?? "GreaterThan");
  const [threshold, setThreshold] = useState(rule?.threshold?.toString() ?? "");
  const [severity, setSeverity] = useState(rule?.severity ?? "Warning");
  const [cooldown, setCooldown] = useState(rule?.cooldownSeconds?.toString() ?? "300");
  const [appliesByDefault, setAppliesByDefault] = useState(rule?.appliesByDefault ?? true);
  const [isEnabled, setIsEnabled] = useState(rule?.isEnabled ?? true);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const allTypes = deviceTypeId === "";
  const type = deviceTypes.find((t) => t.id === deviceTypeId);
  // A rule for one type watches one of its parameters; one for every type, device silence.
  const metricOptions: [string, string][] = allTypes
    ? LIFECYCLE
    : [...(type?.parameters ?? []).map((p): [string, string] => [p.key, `${p.label} (${p.key}${p.unit ? `, ${p.unit}` : ""})${p.kind === "Derived" ? " — calculated" : ""}`]), ...LIFECYCLE];

  const errors: Record<string, string> = {};
  if (!name.trim()) errors.name = "Name is required.";
  if (!metric) errors.metric = allTypes ? "Pick Device offline or No telemetry." : "Pick what to watch.";
  else if (!metricOptions.some(([k]) => k === metric)) errors.metric = `${type?.name ?? "This type"} has no parameter "${metric}".`;
  if (threshold === "" || Number.isNaN(Number(threshold))) errors.threshold = "Enter a number.";
  if (!(Number(cooldown) >= 0)) errors.cooldown = "Zero or more seconds.";
  const shown = submitted ? errors : {};

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitted(true);
    setError(null);
    if (Object.keys(errors).length > 0) return;
    setSaving(true);
    const isLifecycle = LIFECYCLE.some(([k]) => k === metric);
    const body = {
      name: name.trim(),
      // A lifecycle condition applies to every type whichever was picked.
      deviceTypeId: isLifecycle ? null : deviceTypeId || null,
      metric,
      operator,
      threshold: Number(threshold),
      severity,
      cooldownSeconds: Number(cooldown),
      isEnabled,
      appliesByDefault,
    };
    try {
      if (rule) await GlobalAlertRulesApi.update(rule.id, body);
      else await GlobalAlertRulesApi.create(body);
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save the rule");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={rule ? `Edit ${rule.name}` : "New global alert rule"} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="Name" error={shown.name}>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="High Voltage" className={inputClass} />
        </FormField>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Device type">
            <select
              value={deviceTypeId}
              onChange={(e) => {
                setDeviceTypeId(e.target.value);
                setMetric("");
              }}
              className={inputClass}
            >
              {deviceTypes.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
              <option value="">All device types (offline / no telemetry)</option>
            </select>
          </FormField>
          <FormField label="Watch" error={shown.metric}>
            <select value={metric} onChange={(e) => setMetric(e.target.value)} className={inputClass}>
              <option value="">Select…</option>
              {metricOptions.map(([k, label]) => (
                <option key={k} value={k}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
        </div>
        <div className="grid grid-cols-3 gap-3">
          <FormField label="Condition">
            <select value={operator} onChange={(e) => setOperator(e.target.value)} className={inputClass}>
              {OPERATORS.map(([o, symbol]) => (
                <option key={o} value={o}>
                  {symbol} {o}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Threshold" error={shown.threshold}>
            <input type="number" step="any" value={threshold} onChange={(e) => setThreshold(e.target.value)} placeholder="250" className={inputClass} />
          </FormField>
          <FormField label="Severity">
            <select value={severity} onChange={(e) => setSeverity(e.target.value)} className={inputClass}>
              {SEVERITIES.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </FormField>
        </div>
        <FormField label="Cooldown (seconds)" error={shown.cooldown}>
          <input type="number" min={0} value={cooldown} onChange={(e) => setCooldown(e.target.value)} className={inputClass} />
          <p className="text-xs text-muted-foreground mt-1">Minimum time before the same device raises this alert again.</p>
        </FormField>
        <FormField label="Applies to">
          <select value={appliesByDefault ? "all" : "selected"} onChange={(e) => setAppliesByDefault(e.target.value === "all")} className={inputClass}>
            <option value="all">All companies — including new ones (a company can switch it off)</option>
            <option value="selected">Selected companies — only where a company, site or device switches it on</option>
          </select>
        </FormField>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} />
          Enabled (off switches it off everywhere)
        </label>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : rule ? "Save changes" : "Create rule"}
        </button>
      </form>
    </Modal>
  );
}
