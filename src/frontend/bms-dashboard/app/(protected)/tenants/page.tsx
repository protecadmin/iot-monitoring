"use client";

import { useState } from "react";
import { Building2, Plus } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { TenantsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { ErrorState, EmptyState, StatusBadge, TableSkeleton } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { useAuth } from "@/lib/auth/AuthContext";
import type { TenantDto } from "@/types/api";

function CreateTenantModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      await TenantsApi.create({ name, slug });
      onCreated();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to create tenant");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title="New tenant" onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="Name">
          <input required value={name} onChange={(e) => setName(e.target.value)} placeholder="Acme Corp" className={inputClass} />
        </FormField>
        <FormField label="Slug (URL-safe identifier)">
          <input
            required
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder="acme-corp"
            pattern="[a-z0-9\-]+"
            title="Lowercase letters, numbers, and hyphens only"
            className={inputClass}
          />
        </FormField>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Creating..." : "Create tenant"}
        </button>
      </form>
    </Modal>
  );
}

function EditTenantModal({ tenant, onClose, onUpdated }: { tenant: TenantDto; onClose: () => void; onUpdated: () => void }) {
  const [name, setName] = useState(tenant.name);
  const [isActive, setIsActive] = useState(tenant.isActive);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      await TenantsApi.update(tenant.id, { name, isActive });
      onUpdated();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to update tenant");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={`Edit ${tenant.name}`} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="Name">
          <input required value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </FormField>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Active
        </label>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : "Save changes"}
        </button>
      </form>
    </Modal>
  );
}

export default function TenantsPage() {
  const { user } = useAuth();
  const query = useApi(() => TenantsApi.list(), []);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<TenantDto | null>(null);

  if (user?.role !== "SuperAdmin") {
    return <p className="text-sm text-muted-foreground">Only SuperAdmin can view all tenants. You belong to a single tenant, scoped automatically across the app.</p>;
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Tenants</h1>
          <p className="text-sm text-muted-foreground">Customer organizations using this platform.</p>
        </div>
        <button
          onClick={() => setCreating(true)}
          className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
        >
          <Plus className="h-4 w-4" /> New tenant
        </button>
      </div>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <TableSkeleton cols={5} />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!query.data || query.data.items.length === 0) && (
        <EmptyState message="No tenants yet" hint="Create your first tenant to start onboarding sites and devices." icon={Building2} />
      )}

      {!query.loading && !query.error && query.data && query.data.items.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Name</th>
                <th className="text-left px-4 py-2">Slug</th>
                <th className="text-left px-4 py-2">Status</th>
                <th className="text-left px-4 py-2">Created</th>
                <th className="text-left px-4 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {query.data.items.map((t) => (
                <tr key={t.id}>
                  <td className="px-4 py-2 font-medium">{t.name}</td>
                  <td className="px-4 py-2 text-muted-foreground">{t.slug}</td>
                  <td className="px-4 py-2">
                    <StatusBadge status={t.isActive ? "Active" : "Inactive"} />
                  </td>
                  <td className="px-4 py-2 text-muted-foreground text-xs">{new Date(t.createdAt).toLocaleDateString()}</td>
                  <td className="px-4 py-2">
                    <button onClick={() => setEditing(t)} className="text-xs text-primary hover:underline">
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {creating && <CreateTenantModal onClose={() => setCreating(false)} onCreated={query.refetch} />}
      {editing && <EditTenantModal tenant={editing} onClose={() => setEditing(null)} onUpdated={query.refetch} />}
    </div>
  );
}
