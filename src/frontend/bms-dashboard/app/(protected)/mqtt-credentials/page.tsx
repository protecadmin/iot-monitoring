"use client";

import { useMemo, useState } from "react";
import { Check, Copy, Eye, EyeOff, KeyRound, Pencil, RotateCw, Trash2 } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { useApi } from "@/hooks/useApi";
import { MqttCredentialsApi, TenantsApi } from "@/lib/api/resources";
import { ErrorState, EmptyState, TableSkeleton } from "@/components/ui/States";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { ApiError } from "@/lib/api/client";
import type { MqttCredentialDto } from "@/types/api";

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard access denied — nothing else to do, the value is still on screen to select manually.
    }
  }

  return (
    <button onClick={handleCopy} title="Copy to clipboard" className="p-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground">
      {copied ? <Check className="h-3.5 w-3.5 text-primary" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  );
}

function SecretCell({ value }: { value: string }) {
  const [revealed, setRevealed] = useState(false);

  return (
    <div className="flex items-center gap-1.5 font-mono text-xs">
      <span className="min-w-[9rem]">{revealed ? value : "••••••••••••••••"}</span>
      <button onClick={() => setRevealed((r) => !r)} title={revealed ? "Hide" : "Reveal"} className="p-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground">
        {revealed ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
      </button>
      {revealed && <CopyButton value={value} />}
    </div>
  );
}

const MIN_PASSWORD_LENGTH = 12;

/** 24 random bytes, base64 — the same strength the backend's Rotate generates. */
function generatePassword(): string {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

function EditCredentialModal({ credential, onClose, onSaved }: { credential: MqttCredentialDto; onClose: () => void; onSaved: () => void }) {
  const [password, setPassword] = useState("");
  const [revealed, setRevealed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`Password must be at least ${MIN_PASSWORD_LENGTH} characters`);
      return;
    }
    if (/\s/.test(password)) {
      setError("Password can't contain spaces");
      return;
    }
    setSaving(true);
    try {
      await MqttCredentialsApi.setPassword(credential.id, password);
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to save password");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={`Edit MQTT credential — ${credential.tenantName}`} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="Username">
          <input value={credential.username} disabled className={`${inputClass} font-mono opacity-60 cursor-not-allowed`} />
        </FormField>
        <p className="text-xs text-muted-foreground -mt-1">
          Fixed to the company code. This login can use this company&apos;s registered gateway measurement topics and configured custom topics.
        </p>
        <FormField label="New password">
          <div className="flex gap-2">
            <div className="relative flex-1">
              <input
                type={revealed ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={`At least ${MIN_PASSWORD_LENGTH} characters, no spaces`}
                autoComplete="new-password"
                className={`${inputClass} font-mono pr-9`}
              />
              <button
                type="button"
                onClick={() => setRevealed((r) => !r)}
                title={revealed ? "Hide" : "Show"}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 p-1 rounded text-muted-foreground hover:text-foreground"
              >
                {revealed ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
              </button>
            </div>
            <button
              type="button"
              onClick={() => {
                setPassword(generatePassword());
                setRevealed(true);
              }}
              className="text-xs px-2.5 rounded-md border border-input text-foreground hover:bg-muted shrink-0"
            >
              Generate
            </button>
          </div>
        </FormField>
        <p className="text-xs text-destructive/90">
          Takes effect immediately: devices still using the old password are refused on their next connection until updated.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : "Save password"}
        </button>
      </form>
    </Modal>
  );
}

export default function MqttCredentialsPage() {
  const { user } = useAuth();
  const query = useApi(() => MqttCredentialsApi.list(), []);
  const tenantsQuery = useApi(() => TenantsApi.list(1, 100), []);

  const [creatingTenantId, setCreatingTenantId] = useState<string | null>(null);
  const [rotating, setRotating] = useState<MqttCredentialDto | null>(null);
  const [editing, setEditing] = useState<MqttCredentialDto | null>(null);
  const [deleting, setDeleting] = useState<MqttCredentialDto | null>(null);

  const credentialByTenantId = useMemo(() => {
    const map = new Map<string, MqttCredentialDto>();
    for (const c of query.data ?? []) map.set(c.tenantId, c);
    return map;
  }, [query.data]);

  const tenantsWithoutCredential = useMemo(
    () => (tenantsQuery.data?.items ?? []).filter((t) => !credentialByTenantId.has(t.id)),
    [tenantsQuery.data, credentialByTenantId],
  );

  if (user?.role !== "SuperAdmin") {
    return <p className="text-sm text-muted-foreground">Only SuperAdmin accounts can manage MQTT broker credentials.</p>;
  }

  const loading = query.loading || tenantsQuery.loading;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-start gap-2">
          <KeyRound className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">MQTT Credentials</h1>
            <p className="text-xs text-muted-foreground mt-0.5">
              Broker login for each tenant&apos;s devices. The broker verifies these live against this table — a new or rotated credential works immediately, no restart needed.
            </p>
          </div>
        </div>
        {tenantsWithoutCredential.length > 0 && (
          <select
            value=""
            onChange={(e) => e.target.value && setCreatingTenantId(e.target.value)}
            className="text-sm px-3 py-1.5 rounded-md border border-input bg-background text-foreground"
          >
            <option value="">Create for tenant...</option>
            {tenantsWithoutCredential.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <TableSkeleton cols={5} />
        </div>
      )}
      {(query.error || tenantsQuery.error) && <ErrorState message={query.error ?? tenantsQuery.error ?? "Failed to load"} />}
      {!loading && !query.error && !tenantsQuery.error && (!query.data || query.data.length === 0) && (
        <EmptyState message="No MQTT credentials yet" hint="Create one for a tenant above to let its devices connect to the broker." icon={KeyRound} />
      )}

      {!loading && !query.error && query.data && query.data.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Tenant</th>
                <th className="text-left px-4 py-2">Username</th>
                <th className="text-left px-4 py-2">Password</th>
                <th className="text-left px-4 py-2">Rotated</th>
                <th className="text-left px-4 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {query.data.map((c) => (
                <tr key={c.id}>
                  <td className="px-4 py-2 font-medium text-foreground">{c.tenantName}</td>
                  <td className="px-4 py-2 text-muted-foreground font-mono text-xs">{c.username}</td>
                  <td className="px-4 py-2">
                    <SecretCell value={c.password} />
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{c.rotatedAt ? new Date(c.rotatedAt).toLocaleString() : "Never"}</td>
                  <td className="px-4 py-2">
                    <div className="flex items-center gap-3">
                      <button onClick={() => setEditing(c)} className="flex items-center gap-1 text-xs text-primary hover:underline">
                        <Pencil className="h-3 w-3" /> Edit
                      </button>
                      <button onClick={() => setDeleting(c)} className="flex items-center gap-1 text-xs text-destructive hover:underline">
                        <Trash2 className="h-3 w-3" /> Delete
                      </button>
                      <button onClick={() => setRotating(c)} className="flex items-center gap-1 text-xs text-primary hover:underline">
                        <RotateCw className="h-3 w-3" /> Rotate
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {creatingTenantId && (
        <ConfirmModal
          title="Create MQTT credential"
          danger={false}
          confirmLabel="Create"
          message="Generates a new random password for this tenant's devices and stores it so it can be viewed again later. Existing device firmware/simulators will need updating with the new credential."
          onClose={() => setCreatingTenantId(null)}
          onConfirm={async () => {
            await MqttCredentialsApi.create(creatingTenantId);
            query.refetch();
          }}
        />
      )}

      {editing && <EditCredentialModal credential={editing} onClose={() => setEditing(null)} onSaved={query.refetch} />}

      {deleting && (
        <ConfirmModal
          title="Delete MQTT credential"
          confirmLabel="Delete"
          message={`Delete the broker login for "${deleting.tenantName}" (${deleting.username})? Every device of this tenant is refused on its next connection. Connections already open stay up until they drop. You can create a new credential for this tenant again at any time from "Create for tenant…".`}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await MqttCredentialsApi.remove(deleting.id);
            query.refetch();
          }}
        />
      )}

      {rotating && (
        <ConfirmModal
          title="Rotate MQTT credential"
          danger
          confirmLabel="Rotate"
          message={`This generates a brand new password for "${rotating.tenantName}" and invalidates the old one immediately — every device using the old credential will be disconnected until updated.`}
          onClose={() => setRotating(null)}
          onConfirm={async () => {
            await MqttCredentialsApi.rotate(rotating.id);
            query.refetch();
          }}
        />
      )}
    </div>
  );
}
