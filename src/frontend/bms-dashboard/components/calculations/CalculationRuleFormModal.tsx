"use client";

import { useMemo, useState } from "react";
import { CalculationsApi, DeviceTypesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { FormulaInput, type FormulaParameter } from "@/components/calculations/FormulaInput";
import { UnreportedKeysWarning } from "@/components/calculations/UnreportedKeysWarning";
import {
  FORMULA_KEY_PATTERN,
  FORMULA_TYPE,
  BASELINE_TYPE,
  OUTPUT_KEY_PATTERN,
  RULE_KEY_PATTERN,
  WINDOW_DEFAULT_SECONDS,
  WINDOW_MAX_SECONDS,
  WINDOW_MIN_SECONDS,
  WINDOW_PRESETS,
  formulaVariables,
  isWindowedType,
  isEligibleSource,
  validateFormula,
} from "@/lib/calculations";
import type { CalculationAssignmentMode, CalculationRuleDto, CalculationTypeDto, DeviceDto, DeviceTypeDto, DeviceTypeParameterDto, SiteDto } from "@/types/api";

function toKebab(value: string): string {
  return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 63);
}

function toCamel(value: string): string {
  const words = value.trim().split(/[^A-Za-z0-9]+/).filter(Boolean);
  const joined = words.map((w, i) => (i === 0 ? w.charAt(0).toLowerCase() + w.slice(1) : w.charAt(0).toUpperCase() + w.slice(1))).join("");
  return /^[A-Za-z]/.test(joined) ? joined : joined ? `v${joined}` : "";
}

/**
 * The one form for every calculation. "Only this company" saves a calculation
 * rule of the tenant. "Every company" (standard) saves the calculation onto a
 * Derived parameter of the device type, which the server copies into every
 * tenant's rules — including tenants added later.
 */
export function CalculationRuleFormModal({
  tenantId,
  rule,
  standardParameter,
  canStandard = false,
  types,
  deviceTypes,
  sites,
  devices,
  onClose,
  onSaved,
}: {
  /** Undefined under "All tenants": only a standard calculation can be made then. */
  tenantId?: string;
  rule?: CalculationRuleDto;
  /** Editing a standard calculation: the Derived parameter that holds it. */
  standardParameter?: { deviceType: DeviceTypeDto; parameter: DeviceTypeParameterDto };
  /** May make standard calculations (they change every company — SuperAdmin). */
  canStandard?: boolean;
  types: CalculationTypeDto[];
  deviceTypes: DeviceTypeDto[];
  sites: SiteDto[];
  devices: DeviceDto[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const sp = standardParameter;
  const calc = sp?.parameter.calculation;
  const isEdit = !!rule || !!sp;
  const [standard, setStandard] = useState(!!sp || (!rule && canStandard && !tenantId));
  const [name, setName] = useState(rule?.name ?? sp?.parameter.label ?? "");
  const [key, setKey] = useState(rule?.key ?? "");
  const [keyTouched, setKeyTouched] = useState(isEdit);
  const [type, setType] = useState(rule?.type ?? calc?.type ?? types[0]?.type ?? "TOTALIZER_DELTA");
  const [deviceTypeId, setDeviceTypeId] = useState(rule?.deviceTypeId ?? sp?.deviceType.id ?? "");
  const [siteId, setSiteId] = useState(rule?.siteId ?? "");
  const [deviceId, setDeviceId] = useState(rule?.deviceId ?? "");
  const [sourceParameterKey, setSourceParameterKey] = useState(rule?.sourceParameterKey ?? calc?.sourceParameterKey ?? "");
  const [expression, setExpression] = useState(rule?.expression ?? calc?.expression ?? "");
  const [outputName, setOutputName] = useState(rule?.outputName ?? sp?.parameter.label ?? "");
  const [outputKey, setOutputKey] = useState(rule?.outputKey ?? sp?.parameter.key ?? "");
  const [outputKeyTouched, setOutputKeyTouched] = useState(isEdit);
  const [outputUnit, setOutputUnit] = useState(rule?.outputUnit ?? sp?.parameter.unit ?? "");
  const [outputUnitTouched, setOutputUnitTouched] = useState(isEdit);
  const [maxDelta, setMaxDelta] = useState((rule?.maxDelta ?? calc?.maxDelta)?.toString() ?? "");
  const [windowSeconds, setWindowSeconds] = useState((rule?.windowSeconds ?? calc?.windowSeconds ?? WINDOW_DEFAULT_SECONDS).toString());
  const [isEnabled, setIsEnabled] = useState(rule?.isEnabled ?? true);
  // New rules default to Selected: they are picked per device in the device form.
  const [assignmentMode, setAssignmentMode] = useState<CalculationAssignmentMode>(rule?.assignmentMode ?? "Selected");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  // Field errors show once a save is attempted, then update as you type.
  const [submitted, setSubmitted] = useState(false);

  const selectedType = types.find((t) => t.type === type);
  const isFormula = type === FORMULA_TYPE;
  const isWindowed = isWindowedType(type);

  // A device pins its own type, so its parameters decide the source options
  // even when no device type was picked explicitly.
  const effectiveTypeId = deviceTypeId || devices.find((d) => d.id === deviceId)?.deviceTypeId || "";

  // Each type takes its own kind of source (a Counter for a Totalizer Delta,
  // a measured level for an average or a baseline) — by declared Kind, never
  // unit, matching the backend check.
  const sourceOptions = useMemo(() => {
    const scoped = effectiveTypeId ? deviceTypes.filter((t) => t.id === effectiveTypeId) : deviceTypes;
    const byKey = new Map<string, { key: string; label: string; unit: string | null; typeNames: string[] }>();
    for (const t of scoped) {
      for (const p of t.parameters) {
        if (!isEligibleSource(type, p.kind)) continue;
        const entry = byKey.get(p.key) ?? { key: p.key, label: p.label, unit: p.unit, typeNames: [] };
        entry.typeNames.push(t.name);
        byKey.set(p.key, entry);
      }
    }
    return Array.from(byKey.values()).sort((a, b) => a.key.localeCompare(b.key));
  }, [deviceTypes, effectiveTypeId, type]);

  // A formula may use any measured parameter in scope — never a Derived one,
  // which isn't in the reading the formula is evaluated against.
  const formulaParameters = useMemo(() => {
    const scoped = effectiveTypeId ? deviceTypes.filter((t) => t.id === effectiveTypeId) : deviceTypes;
    const byKey = new Map<string, FormulaParameter>();
    for (const t of scoped) for (const p of t.parameters) if (p.kind !== "Derived" && !byKey.has(p.key)) byKey.set(p.key, p);
    return Array.from(byKey.values()).sort((a, b) => a.key.localeCompare(b.key));
  }, [deviceTypes, effectiveTypeId]);

  const deviceOptions = devices.filter((d) => (!siteId || d.siteId === siteId) && (!deviceTypeId || d.deviceTypeId === deviceTypeId));

  function onNameChange(value: string) {
    setName(value);
    if (!keyTouched) setKey(toKebab(value));
    if (!outputKeyTouched) setOutputKey(toCamel(value));
    if (!outputName || outputName === name) setOutputName(value);
  }

  function onSourceChange(value: string) {
    setSourceParameterKey(value);
    const option = sourceOptions.find((o) => o.key === value);
    if (option && !outputUnitTouched) setOutputUnit(option.unit ?? "");
  }

  const sourceMissing = !!sourceParameterKey && !sourceOptions.some((o) => o.key === sourceParameterKey);

  // The same checks the API makes (CalculationsController.ValidateAsync), so
  // a mistake is named on its field instead of coming back from the server.
  const fieldErrors = useMemo(() => {
    const scoped = effectiveTypeId ? deviceTypes.filter((t) => t.id === effectiveTypeId) : deviceTypes;
    const measuredKeys = new Set(scoped.flatMap((t) => t.parameters.filter((p) => p.kind !== "Derived").map((p) => p.key.toLowerCase())));
    const errors: Partial<Record<"deviceType" | "name" | "key" | "source" | "formula" | "outputName" | "outputKey" | "outputUnit" | "maxDelta" | "windowSeconds", string>> = {};

    if (!name.trim()) errors.name = "Name is required.";
    else if (name.trim().length > 128) errors.name = "Name can be at most 128 characters.";

    if (standard && !deviceTypeId) errors.deviceType = "Pick the device type this calculation is for.";

    if (!standard && !isEdit && !RULE_KEY_PATTERN.test(key.trim().toLowerCase()))
      errors.key = "2–63 characters: lowercase letters, digits or hyphens, starting with a letter or digit (e.g. active-power).";

    if (isFormula) {
      const formulaError = validateFormula(
        expression,
        formulaParameters.filter((p) => FORMULA_KEY_PATTERN.test(p.key)).map((p) => p.key),
        outputKey.trim(),
      );
      if (formulaError) {
        errors.formula = formulaError;
      } else if (!effectiveTypeId) {
        // Without a device type, the parameters may come from different types —
        // but the formula is evaluated on one device's reading.
        const used = formulaVariables(expression).map((k) => k.toLowerCase());
        const fits = scoped.some((t) => used.every((k) => t.parameters.some((p) => p.kind !== "Derived" && p.key.toLowerCase() === k)));
        if (!fits) errors.formula = "No single device type has all of these parameters. Pick a Device type in the scope, or use parameters one type has.";
      }
    } else if (!sourceParameterKey) {
      errors.source = "Pick the parameter this value is calculated from.";
    } else if (sourceMissing) {
      errors.source = `"${sourceParameterKey}" can't feed this type in this scope — pick another source.`;
    }

    if (standard) {
      // Saved as a parameter of the type: any other parameter already owns this key.
      const taken = scoped[0]?.parameters.find((p) => p.key.toLowerCase() === outputKey.trim().toLowerCase());
      if (taken && taken.id !== sp?.parameter.id && (taken.kind !== "Derived" || taken.calculation))
        errors.outputKey = `${scoped[0].name} already has "${taken.key}"${taken.kind === "Derived" ? " — edit that calculation instead" : ""}.`;
    } else if (!outputName.trim()) errors.outputName = "Output name is required.";
    else if (outputName.trim().length > 128) errors.outputName = "Output name can be at most 128 characters.";

    const out = outputKey.trim();
    if (!out) errors.outputKey = "Output key is required.";
    else if (!OUTPUT_KEY_PATTERN.test(out)) errors.outputKey = "Start with a letter, then only letters, digits or underscores (e.g. activePower).";
    else if (!isFormula && out.toLowerCase() === sourceParameterKey.toLowerCase()) errors.outputKey = "Must differ from the source parameter.";
    else if (!errors.outputKey && measuredKeys.has(out.toLowerCase())) errors.outputKey = `"${out}" is already a measured parameter — choose another key.`;

    if (outputUnit.trim().length > 32) errors.outputUnit = "At most 32 characters.";

    if (type === "TOTALIZER_DELTA" && maxDelta.trim() && !(Number(maxDelta) > 0)) errors.maxDelta = "Must be a number greater than zero, or left empty.";

    if (isWindowed) {
      const window = Number(windowSeconds);
      if (!Number.isInteger(window) || window < WINDOW_MIN_SECONDS || window > WINDOW_MAX_SECONDS)
        errors.windowSeconds = `A whole number of seconds between ${WINDOW_MIN_SECONDS} and ${WINDOW_MAX_SECONDS} (24 hours).`;
    }

    return errors;
  }, [standard, sp, deviceTypeId, type, name, key, isEdit, isFormula, isWindowed, windowSeconds, expression, formulaParameters, outputKey, outputName, outputUnit, sourceParameterKey, sourceMissing, maxDelta, effectiveTypeId, deviceTypes]);
  const hasErrors = Object.keys(fieldErrors).length > 0;
  const shown = submitted ? fieldErrors : {};

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitted(true);
    if (hasErrors) {
      setError("Fix the highlighted fields to save.");
      return;
    }
    if (!standard && !tenantId) return;
    setSaving(true);
    if (standard) {
      await saveStandard();
      setSaving(false);
      return;
    }
    const body = {
      name,
      ...(isFormula
        ? { expression }
        : isWindowed
          ? { sourceParameterKey, windowSeconds: Number(windowSeconds) }
          : type === "TOTALIZER_DELTA"
            ? { sourceParameterKey, maxDelta: maxDelta ? Number(maxDelta) : undefined }
            : { sourceParameterKey }),
      outputKey,
      outputName,
      outputUnit: outputUnit || undefined,
      siteId: siteId || undefined,
      deviceId: deviceId || undefined,
      deviceTypeId: deviceTypeId || undefined,
      assignmentMode,
    };
    try {
      if (rule) await CalculationsApi.updateRule(rule.id, tenantId!, { ...body, isEnabled });
      else await CalculationsApi.createRule(tenantId!, { ...body, key, type });
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save calculation rule");
    } finally {
      setSaving(false);
    }
  }

  async function saveStandard() {
    const deviceType = deviceTypes.find((t) => t.id === deviceTypeId)!;
    const calculation = {
      type,
      expression: isFormula ? expression : undefined,
      sourceParameterKey: isFormula ? undefined : sourceParameterKey,
      maxDelta: type === "TOTALIZER_DELTA" && maxDelta ? Number(maxDelta) : undefined,
      windowSeconds: isWindowed ? Number(windowSeconds) : undefined,
    };
    // A Derived parameter declared without a calculation gets one; otherwise it is added.
    const existing =
      sp?.parameter ?? deviceType.parameters.find((p) => p.kind === "Derived" && !p.calculation && p.key.toLowerCase() === outputKey.trim().toLowerCase());
    try {
      const saved = existing
        ? await DeviceTypesApi.updateParameter(deviceType.id, existing.id, {
            label: name,
            unit: outputUnit || undefined,
            minValue: existing.minValue ?? undefined,
            maxValue: existing.maxValue ?? undefined,
            kind: "Derived",
            dashboardWidget: existing.dashboardWidget ?? null,
            calculation,
          })
        : await DeviceTypesApi.addParameter(deviceType.id, { key: outputKey.trim(), label: name, unit: outputUnit || undefined, kind: "Derived", dashboardWidget: null, calculation });
      onSaved();
      if (saved.warnings?.length) {
        setError(`Saved, but not applied for every company: ${saved.warnings.join("; ")}`);
        return;
      }
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save the calculation");
    }
  }

  const standardTypeName = deviceTypes.find((t) => t.id === deviceTypeId)?.name;

  return (
    <Modal title={isEdit ? `Edit ${name || rule?.name}` : "New calculation"} onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-4">
        {!isEdit && canStandard && (
          <FormField label="Who it applies to">
            <div className="space-y-1.5 text-sm">
              <label className="flex items-start gap-2">
                <input type="radio" checked={standard} onChange={() => setStandard(true)} className="mt-1" />
                <span>
                  Every company — standard
                  <span className="block text-xs text-muted-foreground">Every device of the type, in every company, including companies added later.</span>
                </span>
              </label>
              <label className={`flex items-start gap-2 ${tenantId ? "" : "opacity-50"}`}>
                <input type="radio" checked={!standard} disabled={!tenantId} onChange={() => setStandard(false)} className="mt-1" />
                <span>
                  Only this company
                  <span className="block text-xs text-muted-foreground">
                    {tenantId ? "Its own devices, a site or a single device." : "Pick a company at the top of the sidebar first."}
                  </span>
                </span>
              </label>
            </div>
          </FormField>
        )}
        {standard && (
          <div className="grid grid-cols-2 gap-3">
            <FormField label="Device type" error={shown.deviceType}>
              <select
                value={deviceTypeId}
                disabled={isEdit}
                onChange={(e) => setDeviceTypeId(e.target.value)}
                className={`${inputClass} ${isEdit ? "opacity-60 cursor-not-allowed" : ""}`}
              >
                <option value="">Select…</option>
                {deviceTypes.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Result name" error={shown.name}>
              <input value={name} onChange={(e) => onNameChange(e.target.value)} placeholder="Active Power" className={inputClass} />
            </FormField>
          </div>
        )}
        {!standard && (
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Name" error={shown.name}>
            <input value={name} onChange={(e) => onNameChange(e.target.value)} placeholder="Energy Consumption" className={inputClass} />
          </FormField>
          <FormField label={isEdit ? "Key" : "Key (fixed after creation)"} error={shown.key}>
            <input
              value={key}
              disabled={isEdit}
              onChange={(e) => {
                setKeyTouched(true);
                setKey(e.target.value);
              }}
              placeholder="energy-consumption"
              className={`${inputClass} font-mono ${isEdit ? "opacity-60 cursor-not-allowed" : ""}`}
            />
          </FormField>
        </div>
        )}

        <FormField label="Calculation type">
          <select value={type} disabled={isEdit && !standard} onChange={(e) => setType(e.target.value)} className={`${inputClass} ${isEdit && !standard ? "opacity-60" : ""}`}>
            {types.map((t) => (
              <option key={t.type} value={t.type}>
                {t.name}
              </option>
            ))}
          </select>
          {selectedType && <p className="text-xs text-muted-foreground mt-1">{selectedType.description}</p>}
        </FormField>

        {!standard && (
        <fieldset className="border border-border rounded-md p-3 space-y-3">
          <legend className="text-xs font-medium text-muted-foreground px-1">Scope — which devices this rule can apply to (leave empty for any device in the tenant)</legend>
          <div className="grid grid-cols-3 gap-3">
            <FormField label="Device type">
              <select
                value={deviceTypeId}
                onChange={(e) => {
                  setDeviceTypeId(e.target.value);
                  setDeviceId("");
                }}
                className={inputClass}
              >
                <option value="">Any</option>
                {deviceTypes.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Site">
              <select
                value={siteId}
                onChange={(e) => {
                  setSiteId(e.target.value);
                  setDeviceId("");
                }}
                className={inputClass}
              >
                <option value="">Any</option>
                {sites.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Device">
              <select value={deviceId} onChange={(e) => setDeviceId(e.target.value)} className={inputClass}>
                <option value="">Any</option>
                {deviceOptions.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </FormField>
          </div>
        </fieldset>
        )}

        {!standard && (
        <FormField label="Applies to">
          <select value={assignmentMode} onChange={(e) => setAssignmentMode(e.target.value as CalculationAssignmentMode)} className={inputClass}>
            <option value="Selected">Only devices it&apos;s selected on — choose it in the device form</option>
            <option value="Automatic">Every device in scope automatically — including devices added later</option>
          </select>
          <p className="text-xs text-muted-foreground mt-1">
            {assignmentMode === "Selected"
              ? "The rule appears in the Calculations dropdown when creating or editing a matching device. It calculates only for devices where it is ticked."
              : "Nothing to select per device: every matching device — now and in future — gets this calculation. The device form shows it as already applied."}
          </p>
        </FormField>
        )}

        {isFormula ? (
          <FormField label="Formula" error={shown.formula}>
            <FormulaInput value={expression} onChange={setExpression} parameters={formulaParameters} />
          </FormField>
        ) : (
          <FormField label={`Source parameter — must be ${selectedType?.sourceRequirement ?? "eligible for this type"}`} error={shown.source}>
            <select value={sourceParameterKey} onChange={(e) => onSourceChange(e.target.value)} className={inputClass}>
              <option value="">Select a source...</option>
              {sourceMissing && <option value={sourceParameterKey}>{sourceParameterKey} (not eligible for this type)</option>}
              {sourceOptions.map((o) => (
                <option key={o.key} value={o.key}>
                  {o.label} ({o.key}){o.unit ? ` · ${o.unit}` : ""} — {o.typeNames.join(", ")}
                </option>
              ))}
            </select>
            {sourceOptions.length === 0 && (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
                No eligible parameters in this scope ({selectedType?.sourceRequirement ?? "check the device type"}). Set the parameter&apos;s Kind on the
                Device Types page first.
              </p>
            )}
          </FormField>
        )}
        <UnreportedKeysWarning
          keys={isFormula ? formulaVariables(expression) : sourceParameterKey ? [sourceParameterKey] : []}
          tenantId={tenantId}
          deviceTypeId={effectiveTypeId || undefined}
          siteId={siteId || undefined}
          deviceId={deviceId || undefined}
        />

        <div className={`grid ${standard ? "grid-cols-2" : "grid-cols-3"} gap-3`}>
          {!standard && (
            <FormField label="Output name" error={shown.outputName}>
              <input value={outputName} onChange={(e) => setOutputName(e.target.value)} placeholder="Energy Consumption" className={inputClass} />
            </FormField>
          )}
          <FormField label={standard ? (isEdit ? "Key" : "Key (fixed after creation)") : "Output key"} error={shown.outputKey}>
            <input
              value={outputKey}
              disabled={standard && isEdit}
              onChange={(e) => {
                setOutputKeyTouched(true);
                setOutputKey(e.target.value);
              }}
              placeholder="energyConsumption"
              className={`${inputClass} font-mono ${standard && isEdit ? "opacity-60 cursor-not-allowed" : ""}`}
            />
          </FormField>
          <FormField label={standard ? "Unit" : "Output unit"} error={shown.outputUnit}>
            <input
              value={outputUnit}
              onChange={(e) => {
                setOutputUnitTouched(true);
                setOutputUnit(e.target.value);
              }}
              placeholder="kWh"
              className={inputClass}
            />
          </FormField>
        </div>

        {isWindowed && (
          <FormField label="Averaging window" error={shown.windowSeconds}>
            <select value={windowSeconds} onChange={(e) => setWindowSeconds(e.target.value)} className={inputClass}>
              {WINDOW_PRESETS.map((preset) => (
                <option key={preset.seconds} value={preset.seconds}>
                  {preset.label}
                </option>
              ))}
              {!WINDOW_PRESETS.some((p) => p.seconds.toString() === windowSeconds) && <option value={windowSeconds}>{windowSeconds} seconds</option>}
            </select>
            <p className="text-xs text-muted-foreground mt-1">
              One value is published per window, when the first reading of the next one arrives — so an alert on it fires once per period rather than on every
              passing spike. Windows are aligned to the clock, so a 1-hour window runs from the hour.
            </p>
          </FormField>
        )}

        {type === BASELINE_TYPE && (
          <p className="text-xs text-muted-foreground -mt-1">
            Publishes the reading minus the device&apos;s baseline. Set each device&apos;s baseline on its page (Overview → Baseline), usually right after
            installation; until then this rule produces no value.
          </p>
        )}

        {type === "TOTALIZER_DELTA" && (
          <FormField label="Max delta per reading (optional)" error={shown.maxDelta}>
            <input
              type="number"
              step="any"
              min={0}
              value={maxDelta}
              onChange={(e) => setMaxDelta(e.target.value)}
              placeholder="e.g. 50"
              className={inputClass}
            />
            <p className="text-xs text-muted-foreground mt-1">
              A jump larger than this is treated as a meter reset or replacement rather than counted. A drop is always treated that way. Leave empty to accept
              any increase.
            </p>
          </FormField>
        )}

        {standard && (
          <p className="text-xs text-muted-foreground rounded-md border border-violet-500/30 bg-violet-500/5 p-2">
            Applies automatically to every {standardTypeName ?? "device of this type"} of every company — including companies added later. It is added to
            the device type as a calculated parameter <span className="font-mono">{outputKey || "…"}</span>; each company can switch it off for itself.
          </p>
        )}
        {isEdit && !standard && (
          <label className="flex items-center gap-2 text-sm text-foreground">
            <input type="checkbox" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} />
            Enabled
          </label>
        )}
        {isEdit && (
          <p className="text-xs text-muted-foreground">
            Changing the source, formula, scope, max delta or window restarts live accumulation from the next reading. Historical results are always recalculated
            from raw telemetry.
          </p>
        )}
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : isEdit ? "Save changes" : "Create calculation"}
        </button>
      </form>
    </Modal>
  );
}
