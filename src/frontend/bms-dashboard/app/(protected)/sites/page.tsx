"use client";

import { useState } from "react";
import dynamic from "next/dynamic";
import { MapPin, Plus } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { DevicesApi, SitesApi, TenantsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import type { SiteDto } from "@/types/api";

// Leaflet touches `window`/`document` at module load, which doesn't exist
// during Next.js's server-side render of this "use client" component —
// ssr:false skips that and only mounts the map in the browser.
const DeviceMap = dynamic(() => import("@/components/maps/DeviceMap").then((m) => m.DeviceMap), {
  ssr: false,
  loading: () => <div className="h-[420px] flex items-center justify-center text-sm text-muted-foreground">Loading map...</div>,
});

function SiteFormModal({
  site,
  fixedTenantId,
  onClose,
  onSaved,
}: {
  site?: SiteDto;
  fixedTenantId?: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = !!site;
  const tenantsQuery = useApi(() => (fixedTenantId ? Promise.resolve(undefined) : TenantsApi.list()), [fixedTenantId]);

  const [tenantId, setTenantId] = useState(fixedTenantId ?? "");
  const [code, setCode] = useState(site?.code ?? "");
  const [name, setName] = useState(site?.name ?? "");
  const [address, setAddress] = useState(site?.address ?? "");
  const [latitude, setLatitude] = useState(site?.latitude?.toString() ?? "");
  const [longitude, setLongitude] = useState(site?.longitude?.toString() ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const effectiveTenantId = fixedTenantId ?? tenantId;
    if (!isEdit && !effectiveTenantId) {
      setError("Select a tenant");
      return;
    }

    setSaving(true);
    try {
      const body = {
        name,
        address: address || undefined,
        latitude: latitude ? Number(latitude) : undefined,
        longitude: longitude ? Number(longitude) : undefined,
      };
      if (isEdit) {
        await SitesApi.update(site.id, site.tenantId, body);
      } else {
        await SitesApi.create(effectiveTenantId, { ...body, code });
      }
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save site");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={isEdit ? `Edit ${site.name}` : "New site"} onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-3">
        {!isEdit && (
          <div className="grid grid-cols-2 gap-3">
            {!fixedTenantId && (
              <FormField label="Tenant">
                <select required value={tenantId} onChange={(e) => setTenantId(e.target.value)} className={inputClass}>
                  <option value="">Select...</option>
                  {tenantsQuery.data?.items.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            <FormField label="Code (URL-safe identifier)">
              <input required value={code} onChange={(e) => setCode(e.target.value)} placeholder="chennai-factory" className={inputClass} />
            </FormField>
          </div>
        )}
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Name">
            <input required value={name} onChange={(e) => setName(e.target.value)} placeholder="Chennai Factory" className={inputClass} />
          </FormField>
          <FormField label="Address (optional)">
            <input value={address} onChange={(e) => setAddress(e.target.value)} className={inputClass} />
          </FormField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Latitude">
            <input type="number" step="any" value={latitude} onChange={(e) => setLatitude(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Longitude">
            <input type="number" step="any" value={longitude} onChange={(e) => setLongitude(e.target.value)} className={inputClass} />
          </FormField>
        </div>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : isEdit ? "Save changes" : "Create site"}
        </button>
      </form>
    </Modal>
  );
}

export default function SitesPage() {
  const { effectiveTenantId } = useTenantScope();
  // tenantId is omitted for a SuperAdmin (user.tenantId is null for them) —
  // the API then lists sites across every tenant instead of requiring one.
  const sitesQuery = useApi(() => SitesApi.list(effectiveTenantId), [effectiveTenantId]);
  const canEdit = useCan()("sites.edit");
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<SiteDto | null>(null);

  if (sitesQuery.loading || devicesQuery.loading) return <LoadingState />;
  if (sitesQuery.error) return <ErrorState message={sitesQuery.error} />;

  // One marker per site, not per device — a site's status is "Online" if
  // any of its devices currently are, otherwise "Offline"; devices with no
  // coordinates of their own inherit their site's location anyway (see
  // seed data), so this loses no real information, just the visual clutter
  // of every device at the same site stacking near-identical markers.
  const points = (sitesQuery.data?.items ?? [])
    .filter((s) => s.latitude != null && s.longitude != null)
    .map((s) => {
      const devicesAtSite = (devicesQuery.data?.items ?? []).filter((d) => d.siteId === s.id);
      const status = devicesAtSite.some((d) => d.status === "Online") ? "Online" : "Offline";
      return { id: s.id, label: s.name, latitude: s.latitude!, longitude: s.longitude!, status };
    });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Sites</h1>
          <p className="text-sm text-muted-foreground">Physical locations, mapped by site coordinates.</p>
        </div>
        {canEdit && (
          <button
            onClick={() => setCreating(true)}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
          >
            <Plus className="h-4 w-4" /> New site
          </button>
        )}
      </div>

      <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-foreground mb-3">Site map</h2>
        <DeviceMap points={points} />
      </div>

      {!sitesQuery.data || sitesQuery.data.items.length === 0 ? (
        <EmptyState message="No sites yet" hint={canEdit ? "Create a site to start registering devices at a location." : undefined} icon={MapPin} />
      ) : (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Name</th>
                <th className="text-left px-4 py-2">Code</th>
                <th className="text-left px-4 py-2">Address</th>
                {canEdit && <th className="text-left px-4 py-2">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {sitesQuery.data.items.map((s) => (
                <tr key={s.id}>
                  <td className="px-4 py-2 font-medium">{s.name}</td>
                  <td className="px-4 py-2 text-muted-foreground">{s.code}</td>
                  <td className="px-4 py-2 text-muted-foreground">{s.address ?? "—"}</td>
                  {canEdit && (
                    <td className="px-4 py-2">
                      <button onClick={() => setEditing(s)} className="text-xs text-primary hover:underline">
                        Edit
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {creating && <SiteFormModal fixedTenantId={effectiveTenantId} onClose={() => setCreating(false)} onSaved={sitesQuery.refetch} />}
      {editing && <SiteFormModal site={editing} onClose={() => setEditing(null)} onSaved={sitesQuery.refetch} />}
    </div>
  );
}
