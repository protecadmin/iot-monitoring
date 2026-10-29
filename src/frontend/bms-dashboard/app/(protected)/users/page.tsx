"use client";

import { useMemo, useState } from "react";
import { Plus, Users as UsersIcon } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { useCan } from "@/lib/auth/permissions";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { UsersApi, TenantsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { ErrorState, EmptyState, StatusBadge, TableSkeleton } from "@/components/ui/States";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import type { ManagedUserDto } from "@/types/api";

const ASSIGNABLE_ROLES = ["Admin", "Engineer", "Viewer", "Customer"];

function CreateUserModal({
  fixedTenantId,
  onClose,
  onCreated,
}: {
  fixedTenantId?: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const tenantsQuery = useApi(() => (fixedTenantId ? Promise.resolve(undefined) : TenantsApi.list()), [fixedTenantId]);
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState("Viewer");
  const [tenantId, setTenantId] = useState(fixedTenantId ?? "");
  const [telegramChatId, setTelegramChatId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const effectiveTenantId = fixedTenantId ?? tenantId;
    if (!effectiveTenantId) {
      setError("Select a tenant");
      return;
    }
    setSaving(true);
    try {
      await UsersApi.create({ email, displayName, password, role, tenantId: effectiveTenantId, telegramChatId: telegramChatId || undefined });
      onCreated();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to create user");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title="New user" onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-3">
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
          <FormField label="Email">
            <input
              required
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="jane@customer.example"
              className={inputClass}
            />
          </FormField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Display name">
            <input required value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Jane Doe" className={inputClass} />
          </FormField>
          <FormField label="Role">
            <select value={role} onChange={(e) => setRole(e.target.value)} className={inputClass}>
              {ASSIGNABLE_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </FormField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Temporary password (min. 8 characters)">
            <input required minLength={8} type="text" value={password} onChange={(e) => setPassword(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Telegram Chat ID (optional)">
            <input value={telegramChatId} onChange={(e) => setTelegramChatId(e.target.value)} placeholder="123456789" className={inputClass} />
          </FormField>
        </div>
        <p className="text-xs text-muted-foreground">
          Share this password with the customer directly — there is no email invite flow yet, so send it through a secure channel. Telegram Chat ID
          is obtained by messaging the platform&apos;s bot (see Settings) and comes from Telegram, not something you make up.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Creating..." : "Create user"}
        </button>
      </form>
    </Modal>
  );
}

function EditUserModal({
  user,
  isSelf,
  onClose,
  onSaved,
}: {
  user: ManagedUserDto;
  isSelf: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [displayName, setDisplayName] = useState(user.displayName);
  const [telegramChatId, setTelegramChatId] = useState(user.telegramChatId ?? "");
  const [role, setRole] = useState(user.role);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // SuperAdmins and your own account keep their role — the backend rejects those changes too.
  const canChangeRole = !isSelf && user.role !== "SuperAdmin";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      await UsersApi.update(user.id, {
        displayName,
        telegramChatId: telegramChatId || undefined,
        role: canChangeRole && role !== user.role ? role : undefined,
        password: password || undefined,
      });
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save user");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={`Edit ${user.email}`} onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Display name">
            <input required value={displayName} onChange={(e) => setDisplayName(e.target.value)} className={inputClass} />
          </FormField>
          <FormField label="Role">
            <select value={role} disabled={!canChangeRole} onChange={(e) => setRole(e.target.value)} className={`${inputClass} disabled:opacity-60`}>
              {(ASSIGNABLE_ROLES.includes(user.role) ? ASSIGNABLE_ROLES : [user.role, ...ASSIGNABLE_ROLES]).map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </FormField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="New password (leave blank to keep)">
            <input
              minLength={8}
              type="text"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Min. 8 characters"
              className={inputClass}
            />
          </FormField>
          <FormField label="Telegram Chat ID (optional)">
            <input value={telegramChatId} onChange={(e) => setTelegramChatId(e.target.value)} placeholder="123456789" className={inputClass} />
          </FormField>
        </div>
        <p className="text-xs text-muted-foreground">
          Email and tenant can&apos;t be changed. Setting a new password or changing the role signs the user out of existing sessions — share the
          new password with them through a secure channel.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : "Save changes"}
        </button>
      </form>
    </Modal>
  );
}

export default function UsersPage() {
  const { user } = useAuth();
  const { effectiveTenantId } = useTenantScope();
  const canManage = useCan()("users.manage");

  const query = useApi(() => UsersApi.list(effectiveTenantId), [effectiveTenantId]);
  const tenantsQuery = useApi(() => (user?.role === "SuperAdmin" ? TenantsApi.list() : Promise.resolve(undefined)), [user?.role]);
  const tenantById = useMemo(() => {
    const map = new Map<string, string>();
    for (const t of tenantsQuery.data?.items ?? []) map.set(t.id, t.name);
    return map;
  }, [tenantsQuery.data]);

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<ManagedUserDto | null>(null);
  const [toggling, setToggling] = useState<ManagedUserDto | null>(null);

  if (!canManage) {
    return (
      <p className="text-sm text-muted-foreground">Only SuperAdmin and Admin accounts can manage users.</p>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-start gap-2">
          <UsersIcon className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">Users</h1>
            <p className="text-xs text-muted-foreground mt-0.5">Create and manage logins for your team and customers.</p>
          </div>
        </div>
        <button
          onClick={() => setCreating(true)}
          className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 shrink-0"
        >
          <Plus className="h-4 w-4" /> New user
        </button>
      </div>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <TableSkeleton cols={6} />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!query.data || query.data.length === 0) && (
        <EmptyState message="No users yet" hint="Create the first login for this tenant." icon={UsersIcon} />
      )}

      {!query.loading && !query.error && query.data && query.data.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Email</th>
                <th className="text-left px-4 py-2">Name</th>
                <th className="text-left px-4 py-2">Role</th>
                {user?.role === "SuperAdmin" && <th className="text-left px-4 py-2">Tenant</th>}
                <th className="text-left px-4 py-2">Status</th>
                <th className="text-left px-4 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {query.data.map((u) => (
                <tr key={u.id}>
                  <td className="px-4 py-2 font-medium text-foreground">{u.email}</td>
                  <td className="px-4 py-2 text-muted-foreground">{u.displayName}</td>
                  <td className="px-4 py-2 text-muted-foreground">{u.role}</td>
                  {user?.role === "SuperAdmin" && (
                    <td className="px-4 py-2 text-muted-foreground">{u.tenantId ? tenantById.get(u.tenantId) ?? u.tenantId.slice(0, 8) : "—"}</td>
                  )}
                  <td className="px-4 py-2">
                    <StatusBadge status={u.isActive ? "Active" : "Inactive"} />
                  </td>
                  <td className="px-4 py-2">
                    <div className="flex gap-3">
                      <button onClick={() => setEditing(u)} className="text-xs text-primary hover:underline">
                        Edit
                      </button>
                      {u.id !== user?.id && (
                        <button onClick={() => setToggling(u)} className={`text-xs hover:underline ${u.isActive ? "text-destructive" : "text-primary"}`}>
                          {u.isActive ? "Deactivate" : "Activate"}
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {creating && (
        <CreateUserModal
          fixedTenantId={user?.role === "SuperAdmin" ? undefined : (effectiveTenantId ?? undefined)}
          onClose={() => setCreating(false)}
          onCreated={query.refetch}
        />
      )}
      {editing && <EditUserModal user={editing} isSelf={editing.id === user?.id} onClose={() => setEditing(null)} onSaved={query.refetch} />}
      {toggling && (
        <ConfirmModal
          title={toggling.isActive ? "Deactivate user" : "Activate user"}
          danger={toggling.isActive}
          confirmLabel={toggling.isActive ? "Deactivate" : "Activate"}
          message={
            toggling.isActive
              ? `"${toggling.email}" will no longer be able to sign in. This can be reversed at any time.`
              : `"${toggling.email}" will be able to sign in again.`
          }
          onClose={() => setToggling(null)}
          onConfirm={async () => {
            if (toggling.isActive) await UsersApi.deactivate(toggling.id);
            else await UsersApi.activate(toggling.id);
            query.refetch();
          }}
        />
      )}
    </div>
  );
}
