"use client";

import { Suspense, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Calculator, Plus } from "lucide-react";
import { useCan } from "@/lib/auth/permissions";
import { ruleInputs } from "@/lib/calculations";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { CalculationsApi, DeviceTypesApi, DevicesApi, SitesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { LoadingState, ErrorState } from "@/components/ui/States";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { CalculationRuleFormModal } from "@/components/calculations/CalculationRuleFormModal";
import type { CalculationRuleDto, DeviceTypeDto, DeviceTypeParameterDto } from "@/types/api";

/** A standard calculation: a Derived parameter of a device type, and this company's copy of it (if a company is picked). */
interface Standard {
  deviceType: DeviceTypeDto;
  parameter: DeviceTypeParameterDto;
  rule?: CalculationRuleDto;
}

const sameKey = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

export default function CalculationRulesPage() {
  return (
    <Suspense fallback={<LoadingState />}>
      <CalculationRules />
    </Suspense>
  );
}

function CalculationRules() {
  const can = useCan();
  const { effectiveTenantId } = useTenantScope();
  const canEdit = can("calculations.edit");
  const canDelete = can("calculations.delete");
  // Standard calculations change every company, so only whoever manages device types edits them.
  const canStandard = can("deviceTypes.manage");

  // ?type=<id>&parameter=<key> opens that standard calculation — "Edit calculation" on Device Types links here.
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const linkedType = params.get("type");
  const linkedParameter = params.get("parameter");

  const rulesQuery = useApi(() => (effectiveTenantId ? CalculationsApi.listRules(effectiveTenantId) : Promise.resolve(undefined)), [effectiveTenantId]);
  const typesQuery = useApi(() => CalculationsApi.types(), []);
  const deviceTypesQuery = useApi(() => DeviceTypesApi.list(), []);
  const sitesQuery = useApi(() => (effectiveTenantId ? SitesApi.list(effectiveTenantId) : Promise.resolve(undefined)), [effectiveTenantId]);
  const devicesQuery = useApi(
    () => (effectiveTenantId ? DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }) : Promise.resolve(undefined)),
    [effectiveTenantId],
  );

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<CalculationRuleDto | null>(null);
  const [editingStandard, setEditingStandard] = useState<Standard | null>(null);
  const [deleting, setDeleting] = useState<CalculationRuleDto | null>(null);
  const [deletingStandard, setDeletingStandard] = useState<Standard | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const names = useMemo(() => {
    const map = new Map<string, string>();
    for (const t of deviceTypesQuery.data ?? []) map.set(t.id, t.name);
    for (const s of sitesQuery.data?.items ?? []) map.set(s.id, s.name);
    for (const d of devicesQuery.data?.items ?? []) map.set(d.id, d.name);
    return map;
  }, [deviceTypesQuery.data, sitesQuery.data, devicesQuery.data]);

  // Standard ones come from the device types, so they show even under "All tenants".
  const standards = useMemo<Standard[]>(
    () =>
      (deviceTypesQuery.data ?? []).flatMap((deviceType) =>
        deviceType.parameters
          .filter((p) => p.kind === "Derived")
          .map((parameter) => ({
            deviceType,
            parameter,
            rule: rulesQuery.data?.find((r) => r.definedOnDeviceType && r.deviceTypeId === deviceType.id && sameKey(r.outputKey, parameter.key)),
          })),
      ),
    [deviceTypesQuery.data, rulesQuery.data],
  );
  const companyRules = (rulesQuery.data ?? []).filter((r) => !r.definedOnDeviceType);

  // Open the linked standard calculation once the device types have loaded.
  useEffect(() => {
    if (!linkedType || !linkedParameter || !canStandard) return;
    const match = standards.find((s) => s.deviceType.id === linkedType && sameKey(s.parameter.key, linkedParameter));
    // eslint-disable-next-line react-hooks/set-state-in-effect -- opening a form from the URL once its data has arrived
    if (match) setEditingStandard(match);
  }, [standards, linkedType, linkedParameter, canStandard]);

  const typeName = (type: string) => typesQuery.data?.find((t) => t.type === type)?.name ?? type;

  function scopeLabel(r: CalculationRuleDto): string {
    const parts: string[] = [];
    if (r.deviceId) parts.push(`Device: ${names.get(r.deviceId) ?? "unknown"}`);
    if (r.siteId) parts.push(`Site: ${names.get(r.siteId) ?? "unknown"}`);
    if (r.deviceTypeId) parts.push(`Type: ${names.get(r.deviceTypeId) ?? "unknown"}`);
    return parts.length ? parts.join(" · ") : "All devices";
  }

  async function toggle(rule: CalculationRuleDto) {
    if (!effectiveTenantId) return;
    setToggling(rule.id);
    setActionError(null);
    try {
      await CalculationsApi.updateRule(rule.id, effectiveTenantId, {
        name: rule.name,
        sourceParameterKey: rule.sourceParameterKey,
        expression: rule.expression ?? undefined,
        outputKey: rule.outputKey,
        outputName: rule.outputName,
        outputUnit: rule.outputUnit ?? undefined,
        maxDelta: rule.maxDelta ?? undefined,
        windowSeconds: rule.windowSeconds ?? undefined,
        siteId: rule.siteId ?? undefined,
        deviceId: rule.deviceId ?? undefined,
        deviceTypeId: rule.deviceTypeId ?? undefined,
        isEnabled: !rule.isEnabled,
        assignmentMode: rule.assignmentMode,
      });
      rulesQuery.refetch();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Failed to update the rule");
    } finally {
      setToggling(null);
    }
  }

  function refetchAll() {
    rulesQuery.refetch();
    deviceTypesQuery.refetch();
  }

  const formReady = typesQuery.data && deviceTypesQuery.data;
  const canCreate = (canEdit && !!effectiveTenantId) || canStandard;

  const statusBadge = (enabled: boolean) => (
    <span
      className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${
        enabled ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400" : "bg-muted text-muted-foreground"
      }`}
    >
      {enabled ? "On" : "Off"}
    </span>
  );
  const head = "bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium";

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-start gap-2">
          <Calculator className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">Calculation Rules</h1>
            <p className="text-xs text-muted-foreground mt-0.5">
              Every calculated value is set up here. It is worked out from the readings as they arrive; the readings themselves are never changed.
            </p>
          </div>
        </div>
        {canCreate && (
          <button
            onClick={() => setCreating(true)}
            disabled={!formReady}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50 shrink-0"
          >
            <Plus className="h-4 w-4" /> New calculation
          </button>
        )}
      </div>

      {actionError && <ErrorState message={actionError} />}

      <section className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Standard — every company</h2>
          <p className="text-xs text-muted-foreground">
            Applies to every device of the type in every company.{" "}
            {canStandard ? "Changes here update all companies." : "Only a SuperAdmin can change these; you can switch one off for your company."}
          </p>
        </div>
        {deviceTypesQuery.loading ? (
          <LoadingState />
        ) : deviceTypesQuery.error ? (
          <ErrorState message={deviceTypesQuery.error} />
        ) : standards.length === 0 ? (
          <p className="text-xs text-muted-foreground">No standard calculations yet.</p>
        ) : (
          <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
            <table className="w-full text-sm">
              <thead className={head}>
                <tr>
                  <th className="text-left px-4 py-2">Name</th>
                  <th className="text-left px-4 py-2">Device type</th>
                  <th className="text-left px-4 py-2">Calculation</th>
                  <th className="text-left px-4 py-2">Unit</th>
                  <th className="text-left px-4 py-2">{effectiveTenantId ? "This company" : "Status"}</th>
                  {(canEdit || canStandard) && <th className="text-left px-4 py-2">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {standards.map((s) => {
                  const c = s.parameter.calculation;
                  return (
                    <tr key={s.parameter.id} className={s.rule && !s.rule.isEnabled ? "opacity-60" : undefined}>
                      <td className="px-4 py-2">
                        <div className="font-medium text-foreground">{s.parameter.label}</div>
                        <div className="text-xs text-muted-foreground font-mono">{s.parameter.key}</div>
                      </td>
                      <td className="px-4 py-2 text-muted-foreground">{s.deviceType.name}</td>
                      <td className="px-4 py-2 text-muted-foreground">
                        {c ? (
                          <>
                            <div className="text-xs">{typeName(c.type)}</div>
                            <div className="font-mono text-xs text-foreground/80">
                              {ruleInputs({ expression: c.expression ?? null, sourceParameterKey: c.sourceParameterKey ?? "", windowSeconds: c.windowSeconds ?? null })}
                            </div>
                          </>
                        ) : (
                          <span className="text-xs text-amber-600 dark:text-amber-400">No calculation set yet</span>
                        )}
                      </td>
                      <td className="px-4 py-2 text-muted-foreground">{s.parameter.unit ?? "—"}</td>
                      <td className="px-4 py-2">
                        {!effectiveTenantId ? (
                          <span className="text-xs text-muted-foreground">All companies</span>
                        ) : s.rule ? (
                          statusBadge(s.rule.isEnabled)
                        ) : (
                          <span className="text-xs text-muted-foreground" title="Not calculated for this company — another rule may already produce this key.">
                            Not applied
                          </span>
                        )}
                      </td>
                      {(canEdit || canStandard) && (
                        <td className="px-4 py-2">
                          <div className="flex gap-3 text-xs whitespace-nowrap">
                            {canEdit && s.rule && (
                              <button
                                onClick={() => toggle(s.rule!)}
                                disabled={toggling === s.rule.id}
                                title="Only for this company"
                                className="text-primary hover:underline disabled:opacity-50"
                              >
                                {s.rule.isEnabled ? "Switch off" : "Switch on"}
                              </button>
                            )}
                            {canStandard && (
                              <>
                                <button onClick={() => setEditingStandard(s)} disabled={!formReady} className="text-primary hover:underline disabled:opacity-50">
                                  {c ? "Edit" : "Set calculation"}
                                </button>
                                <button onClick={() => setDeletingStandard(s)} className="text-destructive hover:underline">
                                  Delete
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Only this company</h2>
          <p className="text-xs text-muted-foreground">Calculations for this company&apos;s own devices, a site or a single device.</p>
        </div>
        {!effectiveTenantId ? (
          <p className="text-xs text-muted-foreground">Pick a company (top of the sidebar) to see its own calculations.</p>
        ) : rulesQuery.loading ? (
          <LoadingState />
        ) : rulesQuery.error ? (
          <ErrorState message={rulesQuery.error} />
        ) : companyRules.length === 0 ? (
          <p className="text-xs text-muted-foreground">None yet — use New calculation and choose &quot;Only this company&quot;.</p>
        ) : (
          <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
            <table className="w-full text-sm">
              <thead className={head}>
                <tr>
                  <th className="text-left px-4 py-2">Name</th>
                  <th className="text-left px-4 py-2">Applies to</th>
                  <th className="text-left px-4 py-2">Calculation</th>
                  <th className="text-left px-4 py-2">Unit</th>
                  <th className="text-left px-4 py-2">Status</th>
                  {canEdit && <th className="text-left px-4 py-2">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {companyRules.map((r) => (
                  <tr key={r.id} className={r.isEnabled ? undefined : "opacity-60"}>
                    <td className="px-4 py-2">
                      <div className="font-medium text-foreground">{r.name}</div>
                      <div className="text-xs text-muted-foreground font-mono">{r.outputKey}</div>
                    </td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">
                      <div>{scopeLabel(r)}</div>
                      <div className="mt-0.5">{r.assignmentMode === "Selected" ? "Chosen per device" : "Automatic"}</div>
                    </td>
                    <td className="px-4 py-2 text-muted-foreground">
                      <div className="text-xs">{typeName(r.type)}</div>
                      <div className="font-mono text-xs text-foreground/80">{ruleInputs(r)}</div>
                    </td>
                    <td className="px-4 py-2 text-muted-foreground">{r.outputUnit ?? "—"}</td>
                    <td className="px-4 py-2">{statusBadge(r.isEnabled)}</td>
                    {canEdit && (
                      <td className="px-4 py-2">
                        <div className="flex gap-3 text-xs whitespace-nowrap">
                          <button onClick={() => toggle(r)} disabled={toggling === r.id} className="text-primary hover:underline disabled:opacity-50">
                            {r.isEnabled ? "Switch off" : "Switch on"}
                          </button>
                          <button onClick={() => setEditing(r)} disabled={!formReady} className="text-primary hover:underline disabled:opacity-50">
                            Edit
                          </button>
                          {canDelete && (
                            <button onClick={() => setDeleting(r)} className="text-destructive hover:underline">
                              Delete
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
      </section>

      {(creating || editing || editingStandard) && formReady && (
        <CalculationRuleFormModal
          tenantId={effectiveTenantId ?? undefined}
          rule={editing ?? undefined}
          standardParameter={editingStandard ? { deviceType: editingStandard.deviceType, parameter: editingStandard.parameter } : undefined}
          canStandard={canStandard}
          types={typesQuery.data!}
          deviceTypes={deviceTypesQuery.data!}
          sites={sitesQuery.data?.items ?? []}
          devices={devicesQuery.data?.items ?? []}
          onClose={() => {
            setCreating(false);
            setEditing(null);
            setEditingStandard(null);
            // Closing drops the link, so a refresh doesn't reopen it.
            if (linkedType) router.replace(pathname);
          }}
          onSaved={refetchAll}
        />
      )}
      {deleting && effectiveTenantId && (
        <ConfirmModal
          title="Delete calculation"
          message={`Delete "${deleting.name}"? It stops calculating immediately. Raw telemetry is untouched, so recreating it restores its full history.`}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await CalculationsApi.deleteRule(deleting.id, effectiveTenantId);
            rulesQuery.refetch();
          }}
        />
      )}
      {deletingStandard && (
        <ConfirmModal
          title="Delete standard calculation"
          message={`Delete "${deletingStandard.parameter.label}" from ${deletingStandard.deviceType.name}? It stops calculating for every company and is removed from the device type and its dashboard. Raw telemetry is untouched.`}
          onClose={() => setDeletingStandard(null)}
          onConfirm={async () => {
            await DeviceTypesApi.removeParameter(deletingStandard.deviceType.id, deletingStandard.parameter.id);
            refetchAll();
          }}
        />
      )}
    </div>
  );
}
