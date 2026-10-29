"use client";

import { useState } from "react";
import Link from "next/link";
import { Boxes, ChevronDown, ChevronUp, Pencil, Plus, Trash2 } from "lucide-react";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { DeviceTypeFormModal } from "@/components/devices/DeviceTypeFormModal";
import { formatWindow } from "@/lib/calculations";
import type { DashboardWidget, DeviceTypeDto, DeviceTypeParameterDto, ParameterKind } from "@/types/api";

/** How the dashboard shows a parameter — chosen once per device type, applied to every company. */
const WIDGETS: { value: DashboardWidget | ""; label: string; hint: string }[] = [
  { value: "", label: "Not on the dashboard", hint: "Still charted on the device's own page." },
  { value: "LiveValue", label: "Live value", hint: "A live reading — kW, m³/h, °C. The value and its last-hour trend, one card per device." },
  { value: "LevelGauge", label: "Level gauge", hint: "A 0–100 % level — tank, load. One gauge per device." },
  { value: "SafetyStatus", label: "Safety status", hint: "Good / Caution / Danger from its alert rules — vibration, noise, heat stress. One card per device." },
  { value: "OnOff", label: "On / off", hint: "A state — pump running, door open." },
];

const KINDS: { value: ParameterKind; label: string; hint: string }[] = [
  { value: "Instantaneous", label: "Instantaneous", hint: "A reading of the current state — voltage, temperature, flow rate." },
  { value: "Counter", label: "Counter (totalizer)", hint: "A cumulative count that only grows between resets — energy, water or fuel totalizer, runtime hours. Required for Totalizer Delta calculations." },
  { value: "Digital", label: "Digital", hint: "An on/off or discrete state — run status, door contact." },
  {
    value: "Derived",
    label: "Derived",
    hint: "Calculated from other parameters. Its calculation is set on the Calculation Rules page.",
  },
];

/** How a Derived parameter is calculated, in one line. */
function calculationText(parameter: DeviceTypeParameterDto): string {
  const c = parameter.calculation;
  if (!c) return "not calculated yet";
  if (c.expression) return `= ${c.expression}`;
  return c.windowSeconds ? `from ${c.sourceParameterKey} over ${formatWindow(c.windowSeconds)}` : `from ${c.sourceParameterKey}`;
}

/** Opens this parameter's calculation on Calculation Rules — the one place calculations are edited. */
const calculationLink = (deviceType: DeviceTypeDto, parameter: DeviceTypeParameterDto) =>
  `/calculations/rules?type=${deviceType.id}&parameter=${encodeURIComponent(parameter.key)}`;

/** Under a Derived parameter's Kind badge: how it is calculated, in full on hover. */
function DerivationNote({ parameter }: { parameter: DeviceTypeParameterDto }) {
  const text = calculationText(parameter);
  return (
    <div className="text-[10px] text-muted-foreground mt-0.5 truncate font-mono" title={text}>
      {text}
    </div>
  );
}

function ParameterFormModal({
  deviceType,
  parameter,
  onClose,
  onSaved,
}: {
  deviceType: DeviceTypeDto;
  parameter?: DeviceTypeParameterDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = !!parameter;
  const [key, setKey] = useState(parameter?.key ?? "");
  const [label, setLabel] = useState(parameter?.label ?? "");
  const [unit, setUnit] = useState(parameter?.unit ?? "");
  const [minValue, setMinValue] = useState(parameter?.minValue?.toString() ?? "");
  const [maxValue, setMaxValue] = useState(parameter?.maxValue?.toString() ?? "");
  const [kind, setKind] = useState<ParameterKind>(parameter?.kind ?? "Instantaneous");
  const [widget, setWidget] = useState<DashboardWidget | "">(parameter?.dashboardWidget ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  // Set once the parameter is stored, so saving again after a warning
  // updates it instead of trying to create it a second time.
  const [savedParameterId, setSavedParameterId] = useState<string | undefined>(parameter?.id);

  // A calculated (Derived) parameter's calculation is set on Calculation
  // Rules; this form only keeps it as it is.
  const isDerived = kind === "Derived";

  // Field errors show once a save is attempted, then update as you type.
  const [submitted, setSubmitted] = useState(false);
  const fieldErrors: Partial<Record<"key" | "label" | "range", string>> = {};
  if (!key.trim()) fieldErrors.key = "Key is required.";
  else if (!savedParameterId && deviceType.parameters.some((p) => p.key.toLowerCase() === key.trim().toLowerCase()))
    fieldErrors.key = `${deviceType.name} already has a parameter "${key.trim()}".`;
  if (!label.trim()) fieldErrors.label = "Label is required.";
  if (minValue && maxValue && Number(minValue) > Number(maxValue)) fieldErrors.range = "Min can't be greater than Max.";
  const shown = submitted ? fieldErrors : {};

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitted(true);
    if (Object.keys(fieldErrors).length > 0) {
      setError("Fix the highlighted fields to save.");
      return;
    }
    setSaving(true);
    try {
      const body = {
        label,
        unit: unit || undefined,
        minValue: minValue ? Number(minValue) : undefined,
        maxValue: maxValue ? Number(maxValue) : undefined,
        kind,
        dashboardWidget: widget || null,
        // Sent back unchanged — leaving it out would remove the calculation.
        calculation: isDerived ? (parameter?.calculation ?? undefined) : undefined,
      };
      const saved = savedParameterId
        ? await DeviceTypesApi.updateParameter(deviceType.id, savedParameterId, body)
        : await DeviceTypesApi.addParameter(deviceType.id, { ...body, key });
      setSavedParameterId(saved.id);
      onSaved();
      if (saved.warnings?.length) {
        setError(`Saved, but not applied for every tenant: ${saved.warnings.join("; ")}`);
        return;
      }
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save parameter");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={isEdit ? `Edit ${parameter.key} on ${deviceType.name}` : `New parameter on ${deviceType.name}`} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label={isEdit ? "Key" : "Key (matches the telemetry field name, e.g. current)"} error={shown.key}>
          <input
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="current"
            disabled={!!savedParameterId}
            title={savedParameterId ? "The key can't be changed after creation — it's what ties a device's telemetry field to this parameter." : undefined}
            className={`${inputClass} ${savedParameterId ? "opacity-60 cursor-not-allowed" : ""}`}
          />
        </FormField>
        <FormField label="Label" error={shown.label}>
          <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Current" className={inputClass} />
        </FormField>
        <FormField label="Kind">
          <select
            value={kind}
            disabled={isDerived}
            onChange={(e) => setKind(e.target.value as ParameterKind)}
            className={`${inputClass} ${isDerived ? "opacity-60 cursor-not-allowed" : ""}`}
          >
            {KINDS.filter((k) => k.value !== "Derived" || isDerived).map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </select>
          <p className="text-xs text-muted-foreground mt-1">
            {KINDS.find((k) => k.value === kind)?.hint}{" "}
            {isDerived
              ? "To stop calculating it, delete it on Calculation Rules."
              : "Kind is declared, not guessed from the unit — a kWh parameter is only treated as a totalizer when marked Counter."}
          </p>
          {!isEdit && (
            <p className="text-xs text-muted-foreground mt-1">
              A calculated value (e.g. power from voltage × current)? Add it on{" "}
              <Link href="/calculations/rules" className="text-primary hover:underline">
                Calculation Rules
              </Link>{" "}
              → New calculation.
            </p>
          )}
        </FormField>
        <FormField label="Show on dashboard as">
          <select value={widget} onChange={(e) => setWidget(e.target.value as DashboardWidget | "")} className={inputClass}>
            {WIDGETS.map((w) => (
              <option key={w.value} value={w.value}>
                {w.label}
              </option>
            ))}
          </select>
          <p className="text-xs text-muted-foreground mt-1">{WIDGETS.find((w) => w.value === widget)?.hint} Applies to every company&apos;s devices of this type.</p>
        </FormField>
        {isDerived && parameter && (
          <div className="rounded-md border border-violet-500/30 bg-violet-500/5 p-3 text-xs space-y-1">
            <div className="font-medium text-foreground">Calculated</div>
            <div className="font-mono text-muted-foreground">{calculationText(parameter)}</div>
            <Link href={calculationLink(deviceType, parameter)} className="text-primary hover:underline">
              Edit calculation on Calculation Rules →
            </Link>
          </div>
        )}
        <div className="grid grid-cols-3 gap-3">
          <FormField label="Unit">
            <input value={unit} onChange={(e) => setUnit(e.target.value)} placeholder="A" className={inputClass} />
          </FormField>
          <FormField label="Min">
            <input type="number" step="any" value={minValue} onChange={(e) => setMinValue(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Max" error={shown.range}>
            <input type="number" step="any" value={maxValue} onChange={(e) => setMaxValue(e.target.value)} className={inputClass} />
          </FormField>
        </div>
        <p className="text-xs text-muted-foreground">
          Only fields whose name matches a parameter key defined here are kept for devices of this type — anything else a device sends is dropped
          during ingestion.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : isEdit ? "Save changes" : "Add parameter"}
        </button>
      </form>
    </Modal>
  );
}

function DeviceTypeCard({ deviceType, canManage, onChanged }: { deviceType: DeviceTypeDto; canManage: boolean; onChanged: () => void }) {
  const [expanded, setExpanded] = useState(false);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<DeviceTypeParameterDto | null>(null);
  const [deleting, setDeleting] = useState<DeviceTypeParameterDto | null>(null);
  const [editingType, setEditingType] = useState(false);
  const [deletingType, setDeletingType] = useState(false);

  return (
    <div className="bg-card border border-border rounded-lg shadow-sm">
      {/* The expand toggle and the type actions are siblings, not nested — a button inside a button is invalid HTML. */}
      <div className="flex items-center">
        <button onClick={() => setExpanded((e) => !e)} className="flex-1 min-w-0 flex items-center justify-between gap-3 px-4 py-3 text-left">
          <div className="min-w-0">
            <div className="text-sm font-medium text-foreground">{deviceType.name}</div>
            {deviceType.description && <div className="text-xs text-muted-foreground">{deviceType.description}</div>}
          </div>
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground shrink-0">
            {deviceType.parameters.length} parameter{deviceType.parameters.length === 1 ? "" : "s"}
            {expanded ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
          </div>
        </button>
        {canManage && (
          <div className="flex items-center gap-1 pr-3">
            <button
              onClick={() => setEditingType(true)}
              title="Edit name and description"
              aria-label={`Edit ${deviceType.name}`}
              className="p-1.5 rounded-md text-muted-foreground hover:text-primary hover:bg-muted"
            >
              <Pencil className="h-3.5 w-3.5" />
            </button>
            <button
              onClick={() => setDeletingType(true)}
              title="Delete device type"
              aria-label={`Delete ${deviceType.name}`}
              className="p-1.5 rounded-md text-muted-foreground hover:text-destructive hover:bg-muted"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </div>
        )}
      </div>

      {expanded && (
        <div className="border-t border-border px-4 py-3 space-y-3">
          {deviceType.parameters.length === 0 ? (
            <p className="text-xs text-muted-foreground">No parameters defined yet — every telemetry field this type&apos;s devices send will be dropped until at least one is added.</p>
          ) : (
            // table-fixed + explicit widths on every card's table — without
            // this, each table auto-sizes its own columns from its own
            // content (e.g. "co2" vs "3 phase power factor"), so the same
            // Key/Label/Unit/Range columns land at different x-positions
            // card to card instead of lining up down the page.
            <table className="w-full text-xs border-collapse table-fixed">
              <thead className="text-muted-foreground uppercase">
                <tr className="divide-x divide-border">
                  <th className="text-left py-1 px-3 first:pl-0" style={{ width: canManage ? "18%" : "20%" }}>Key</th>
                  <th className="text-left py-1 px-3" style={{ width: canManage ? "18%" : "20%" }}>Label</th>
                  <th className="text-center py-1 px-3" style={{ width: canManage ? "16%" : "20%" }}>Kind</th>
                  <th className="text-center py-1 px-3" style={{ width: canManage ? "14%" : "20%" }}>Unit</th>
                  <th className="text-center py-1 px-3" style={{ width: canManage ? "16%" : "20%" }}>Range</th>
                  {canManage && <th className="text-center py-1 px-3" style={{ width: "18%" }}>Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {deviceType.parameters.map((p) => (
                  <tr key={p.id} className="divide-x divide-border">
                    <td className="py-1.5 px-3 first:pl-0 font-mono">{p.key}</td>
                    <td className="py-1.5 px-3">{p.label}</td>
                    <td className="py-1.5 px-3 text-center">
                      <span
                        className={`inline-block px-1.5 py-0.5 rounded text-[10px] font-medium ${
                          p.kind === "Counter"
                            ? "bg-primary/10 text-primary"
                            : p.kind === "Derived"
                              ? "bg-violet-500/10 text-violet-600 dark:text-violet-400"
                              : "bg-muted text-muted-foreground"
                        }`}
                      >
                        {p.kind ?? "Instantaneous"}
                      </span>
                      {p.kind === "Derived" && <DerivationNote parameter={p} />}
                    </td>
                    <td className="py-1.5 px-3 text-center text-muted-foreground">{p.unit ?? "—"}</td>
                    <td className="py-1.5 px-3 text-center text-muted-foreground">
                      {p.minValue ?? "—"} .. {p.maxValue ?? "—"}
                    </td>
                    {canManage && (
                      <td className="py-1.5 px-3 text-center space-x-3">
                        <button onClick={() => setEditing(p)} className="text-primary hover:underline">
                          Edit
                        </button>
                        {p.kind === "Derived" && (
                          <Link href={calculationLink(deviceType, p)} className="text-primary hover:underline" title="Edit its calculation on Calculation Rules">
                            Calculation
                          </Link>
                        )}
                        <button onClick={() => setDeleting(p)} className="text-destructive hover:underline">
                          Delete
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {canManage && (
            <button onClick={() => setAdding(true)} className="text-xs text-primary hover:underline">
              + Add parameter
            </button>
          )}
        </div>
      )}

      {editingType && <DeviceTypeFormModal deviceType={deviceType} onClose={() => setEditingType(false)} onSaved={onChanged} />}
      {deletingType && (
        <ConfirmModal
          title="Delete device type"
          message={`Delete "${deviceType.name}" and its ${deviceType.parameters.length} parameter${deviceType.parameters.length === 1 ? "" : "s"}? This is only allowed when no device, firmware or rule uses it, and can't be undone.`}
          onClose={() => setDeletingType(false)}
          onConfirm={async () => {
            await DeviceTypesApi.remove(deviceType.id);
            onChanged();
          }}
        />
      )}
      {adding && <ParameterFormModal deviceType={deviceType} onClose={() => setAdding(false)} onSaved={onChanged} />}
      {editing && (
        <ParameterFormModal
          deviceType={deviceType}
          parameter={editing}
          onClose={() => setEditing(null)}
          onSaved={onChanged}
        />
      )}
      {deleting && (
        <ConfirmModal
          title="Delete parameter"
          message={`Delete "${deleting.label}" (${deleting.key})? Devices of this type will stop storing this field. Historical data already recorded isn't affected.`}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await DeviceTypesApi.removeParameter(deviceType.id, deleting.id);
            onChanged();
          }}
        />
      )}
    </div>
  );
}

export default function DeviceTypesPage() {
  const query = useApi(() => DeviceTypesApi.list(), []);
  const [creating, setCreating] = useState(false);
  const can = useCan();
  const canManage = can("deviceTypes.manage");
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-start gap-2">
          <Boxes className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">Device Types</h1>
            <p className="text-xs text-muted-foreground mt-0.5">
              Each type&apos;s parameters define which telemetry fields its devices are allowed to store — everything else a device sends is dropped.
            </p>
          </div>
        </div>
        {canManage && (
          <button
            onClick={() => setCreating(true)}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 shrink-0"
          >
            <Plus className="h-4 w-4" /> New device type
          </button>
        )}
      </div>

      {query.loading && <LoadingState />}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!query.data || query.data.length === 0) && (
        <EmptyState message="No device types yet" hint="Create a device type to start defining its telemetry parameters." icon={Boxes} />
      )}

      <div className="space-y-2">
        {query.data?.map((dt) => (
          <DeviceTypeCard key={dt.id} deviceType={dt} canManage={canManage} onChanged={query.refetch} />
        ))}
      </div>

      {creating && <DeviceTypeFormModal onClose={() => setCreating(false)} onSaved={() => query.refetch()} />}
    </div>
  );
}
