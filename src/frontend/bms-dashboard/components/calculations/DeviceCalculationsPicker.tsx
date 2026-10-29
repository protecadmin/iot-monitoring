"use client";

import { useEffect, useRef, useState } from "react";
import { Calculator, ChevronDown } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { CalculationsApi } from "@/lib/api/resources";
import { ruleInputs } from "@/lib/calculations";

/**
 * The device form's Calculations dropdown. Options come from the server
 * (which rules fit this device type and site), so the form never offers a rule
 * the backend would refuse. Automatic rules are shown ticked and locked — they
 * already apply to every matching device; only Selected rules are choosable.
 */
export function DeviceCalculationsPicker({
  tenantId,
  deviceTypeId,
  siteId,
  deviceId,
  value,
  onChange,
}: {
  tenantId: string;
  deviceTypeId: string;
  siteId?: string;
  deviceId?: string;
  value: string[];
  onChange: (ruleIds: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const eligibleQuery = useApi(
    () => (tenantId && deviceTypeId ? CalculationsApi.eligible(tenantId, deviceTypeId, siteId || undefined, deviceId) : Promise.resolve(undefined)),
    [tenantId, deviceTypeId, siteId, deviceId],
  );
  const eligible = eligibleQuery.data ?? [];
  const automatic = eligible.filter((e) => e.appliesAutomatically);
  const selectable = eligible.filter((e) => !e.appliesAutomatically);

  // Changing the device type or site can make earlier choices invalid — drop
  // them rather than submitting ids the backend would reject.
  useEffect(() => {
    if (!eligibleQuery.data) return;
    const allowed = new Set(eligibleQuery.data.filter((e) => !e.appliesAutomatically).map((e) => e.rule.id));
    const pruned = value.filter((id) => allowed.has(id));
    if (pruned.length !== value.length) onChange(pruned);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [eligibleQuery.data]);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  if (!tenantId || !deviceTypeId) {
    return <p className="text-xs text-muted-foreground">Select a tenant and device type first.</p>;
  }
  if (eligibleQuery.loading) return <p className="text-xs text-muted-foreground">Loading calculations...</p>;
  if (eligibleQuery.error) return <p className="text-xs text-destructive">{eligibleQuery.error}</p>;
  if (eligible.length === 0) {
    return (
      <p className="text-xs text-muted-foreground">
        No calculations available for this device type. Mark a totalizer&apos;s Kind as Counter on Device Types, then add a rule under Calculation Rules.
      </p>
    );
  }

  const summary = [
    value.length ? `${value.length} selected` : null,
    automatic.length ? `${automatic.length} automatic` : null,
  ]
    .filter(Boolean)
    .join(" · ") || "None selected";

  function toggle(id: string) {
    onChange(value.includes(id) ? value.filter((v) => v !== id) : [...value, id]);
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="w-full flex items-center justify-between gap-2 text-sm px-3 py-1.5 rounded-md border border-border bg-background text-foreground hover:bg-muted/50"
      >
        <span className="flex items-center gap-2">
          <Calculator className="h-3.5 w-3.5 text-muted-foreground" />
          {summary}
        </span>
        <ChevronDown className={`h-4 w-4 text-muted-foreground transition-transform ${open ? "rotate-180" : ""}`} />
      </button>

      {open && (
        <div className="absolute z-20 mt-1 w-full max-h-64 overflow-y-auto rounded-md border border-border bg-card shadow-lg p-1">
          {selectable.map(({ rule }) => (
            <label key={rule.id} className="flex items-start gap-2 px-2 py-1.5 rounded hover:bg-muted/60 cursor-pointer">
              <input type="checkbox" className="mt-0.5" checked={value.includes(rule.id)} onChange={() => toggle(rule.id)} />
              <span className="text-sm">
                <span className="text-foreground">{rule.name}</span>
                {!rule.isEnabled && <span className="ml-1 text-xs text-amber-600 dark:text-amber-400">(rule disabled)</span>}
                <span className="block text-xs text-muted-foreground">
                  {ruleInputs(rule)} → {rule.outputName}
                  {rule.outputUnit ? ` (${rule.outputUnit})` : ""}
                </span>
              </span>
            </label>
          ))}
          {automatic.map(({ rule }) => (
            <div
              key={rule.id}
              className="flex items-start gap-2 px-2 py-1.5 opacity-70"
              title={
                rule.definedOnDeviceType
                  ? "Standard for every company — change it or switch it off for this company on Calculation Rules"
                  : "Applies automatically to every matching device — change this on the Calculation Rules page"
              }
            >
              <input type="checkbox" className="mt-0.5" checked disabled />
              <span className="text-sm">
                <span className="text-foreground">{rule.name}</span>
                <span className="ml-1.5 inline-block px-1.5 py-0.5 rounded bg-primary/10 text-primary text-[10px] font-medium">Automatic</span>
                <span className="block text-xs text-muted-foreground">
                  {ruleInputs(rule)} → {rule.outputName}
                  {rule.outputUnit ? ` (${rule.outputUnit})` : ""}
                </span>
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
