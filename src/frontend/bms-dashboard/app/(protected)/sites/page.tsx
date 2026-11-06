"use client";

import { useState } from "react";
import dynamic from "next/dynamic";
import { MapPin, Plus, Search, Pencil, Map as MapIcon } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useCan } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { SitesApi, TenantsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { EmptyState } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import type { SiteDto } from "@/types/api";

// Leaflet touches `window`/`document` at module load, which doesn't exist
// during Next.js's server-side render of this "use client" component —
// ssr:false skips that and only mounts the map in the browser.
const SiteMap = dynamic(() => import("@/components/sites/SiteMap"), {
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
            <input type="number" step="any" min={-90} max={90} value={latitude} onChange={(e) => setLatitude(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Longitude">
            <input type="number" step="any" min={-180} max={180} value={longitude} onChange={(e) => setLongitude(e.target.value)} className={inputClass} />
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

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<SiteDto | null>(null);
  const [search, setSearch] = useState("");


  // Plot only valid site coordinates; sites without locations stay in the table.
  const sites = sitesQuery.data?.items ?? [];
  const filtered = sites.filter(s => [s.name, s.code, s.address].some(value => value?.toLowerCase().includes(search.trim().toLowerCase())));
  const points = filtered.filter(s => s.latitude != null && s.longitude != null && Number.isFinite(s.latitude) && Number.isFinite(s.longitude) && Math.abs(s.latitude) <= 90 && Math.abs(s.longitude) <= 180);
  const card = "rounded-xl border border-border bg-card shadow-sm overflow-hidden";

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap gap-3 items-center justify-between border-b border-border pb-4">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Sites</h1>
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

      {sitesQuery.error ? <div className={`${card} p-6 text-center`} role="alert"><p className="font-semibold">Unable to load sites.</p><button onClick={sitesQuery.refetch} className="mt-3 rounded-md border border-border px-4 py-2 text-sm">Retry</button></div> : <>
      <section className={card} aria-label="Site map" aria-busy={sitesQuery.loading}>
        <header className="flex flex-wrap gap-2 items-center justify-between border-b border-border px-5 py-4"><h2 className="flex items-center gap-2 font-semibold"><MapIcon size={18} />Site map</h2>{!sitesQuery.loading && <p className="text-xs text-muted-foreground">{sites.length} sites · {points.length} mapped{search && " (filtered)"}</p>}</header>
        {sitesQuery.loading ? <div role="status" className="h-[280px] sm:h-[320px] lg:h-[400px] bg-muted/40 animate-pulse flex items-center justify-center text-sm text-muted-foreground">Loading site locations…</div> : points.length ? <SiteMap sites={points} onEdit={canEdit ? setEditing : undefined} /> : <div className="py-12 sm:py-16 text-center px-4"><MapPin className="mx-auto mb-3 text-primary" size={30} /><h3 className="font-semibold">No site locations</h3><p className="mt-1 text-sm text-muted-foreground">Add latitude and longitude to a site to display it on the map.</p>{canEdit && <button onClick={() => setCreating(true)} className="mt-4 inline-flex gap-2 items-center border border-border rounded-lg px-3 py-2 text-sm"><Plus size={16} />Add site</button>}</div>}
      </section>

      <section className={card} aria-label="Sites table" aria-busy={sitesQuery.loading}>
      <header className="flex flex-wrap gap-3 items-center justify-between px-5 py-4 border-b border-border"><h2 className="font-semibold">Sites</h2><label className="flex items-center gap-2 rounded-lg border border-border px-3 py-2"><Search size={16} className="text-muted-foreground" /><span className="sr-only">Search sites</span><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search sites" className="bg-transparent outline-none text-sm w-44 max-w-full" /></label></header>
      {sitesQuery.loading ? <div role="status" className="p-5 space-y-3"><span className="sr-only">Loading sites</span>{[0, 1, 2].map(row => <div key={row} className="h-10 rounded bg-muted/50 animate-pulse" />)}</div> : filtered.length === 0 ? (
        <div className="pb-5 text-center"><EmptyState message={search ? "No sites match your current filters." : "No sites found"} hint={search ? undefined : "Create your first site to start mapping devices and gateways."} icon={MapPin} />{search ? <button className="border border-border rounded-lg px-3 py-2 text-sm" onClick={() => setSearch("")}>Clear filters</button> : canEdit && <button className="border border-border rounded-lg px-3 py-2 text-sm" onClick={() => setCreating(true)}>+ New site</button>}</div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[580px] text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Name</th>
                <th className="text-left px-4 py-2">Code</th>
                <th className="text-left px-4 py-2">Address</th>
                {canEdit && <th className="text-left px-4 py-2">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {filtered.map((s) => (
                <tr key={s.id} className="hover:bg-green-50/40">
                  <td className="px-4 py-3 font-semibold">{s.name}</td>
                  <td className="px-4 py-2 text-muted-foreground">{s.code}</td>
                  <td className="px-4 py-2 text-muted-foreground">{s.address ?? "—"}</td>
                  {canEdit && (
                    <td className="px-4 py-2">
                      <button onClick={() => setEditing(s)} aria-label={`Edit ${s.name}`} className="inline-flex items-center gap-1.5 text-xs text-primary hover:underline">
                        <Pencil size={13} />Edit
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      </section>
      </>}

      {creating && <SiteFormModal fixedTenantId={effectiveTenantId} onClose={() => setCreating(false)} onSaved={sitesQuery.refetch} />}
      {editing && <SiteFormModal site={editing} onClose={() => setEditing(null)} onSaved={sitesQuery.refetch} />}
    </div>
  );
}
