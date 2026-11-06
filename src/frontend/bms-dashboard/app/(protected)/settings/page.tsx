"use client";

import { useEffect, useState } from "react";
import { KeyRound, Mail, MessageCircle, Send, Settings as SettingsIcon, User } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { roleLabel } from "@/lib/auth/permissions";
import { useApi } from "@/hooks/useApi";
import { SmtpSettingsApi, TelegramSettingsApi, TenantsApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { FormField, Modal, inputClass } from "@/components/ui/Modal";
import { LoadingState, ErrorState, StatusBadge } from "@/components/ui/States";

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (checked: boolean) => void; label: string }) {
  return (
    <label className="flex items-center gap-2.5 text-sm text-foreground cursor-pointer select-none">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={`settings-toggle relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${checked ? "is-on" : "is-off"}`}
      >
        <span className={`settings-toggle-thumb inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${checked ? "translate-x-[18px]" : "translate-x-1"}`} />
      </button>
      {label}
    </label>
  );
}

function NotificationsSection() {
  const query = useApi(() => SmtpSettingsApi.get(), []);

  const [host, setHost] = useState("");
  const [port, setPort] = useState(587);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [enableSsl, setEnableSsl] = useState(true);
  const [fromAddress, setFromAddress] = useState("");
  const [fromName, setFromName] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const [testEmail, setTestEmail] = useState("");
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ ok: boolean; message: string } | null>(null);

  // Seed the form once the saved settings load — password stays blank
  // (never sent to the browser); hasPassword drives the placeholder instead.
  useEffect(() => {
    if (!query.data) return;
    setHost(query.data.host);
    setPort(query.data.port);
    setUsername(query.data.username);
    setEnableSsl(query.data.enableSsl);
    setFromAddress(query.data.fromAddress);
    setFromName(query.data.fromName);
  }, [query.data]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setSaveError(null);
    setSaved(false);
    setSaving(true);
    try {
      await SmtpSettingsApi.update({
        host,
        port,
        username,
        password: password || undefined,
        enableSsl,
        fromAddress,
        fromName,
      });
      setPassword("");
      setSaved(true);
      query.refetch();
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Failed to save SMTP settings");
    } finally {
      setSaving(false);
    }
  }

  async function handleSendTest() {
    setTestResult(null);
    setTesting(true);
    try {
      await SmtpSettingsApi.sendTest(testEmail);
      setTestResult({ ok: true, message: `Sent to ${testEmail}.` });
    } catch (err) {
      setTestResult({ ok: false, message: err instanceof ApiError ? err.message : "Failed to send test email" });
    } finally {
      setTesting(false);
    }
  }

  return (
    <section className="settings-notification space-y-2">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-foreground flex items-center gap-1.5">
          <Mail className="h-4 w-4 text-muted-foreground" /> Notifications (SMTP)
        </h2>
        {query.data && <StatusBadge status={query.data.host ? "Configured" : "Not configured"} />}
      </div>
      <p className="text-xs text-muted-foreground">
        Emails a tenant&apos;s Company Admin and assigned Site Admin users when a Critical-severity alert fires. Leave Host blank to keep email alerts disabled —
        everything still shows up live in the dashboard either way.
      </p>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm">
          <LoadingState />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}

      {!query.loading && !query.error && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <form onSubmit={handleSave} className="p-4 space-y-3">
            {/* One 4-column grid for every row, not fields stacked two-per-row
                — at full page width that left each input stretching to
                600-800px, awkward for a single-line value like a hostname. */}
            <div className="grid grid-cols-4 gap-3">
              <FormField label="Host">
                <input value={host} onChange={(e) => setHost(e.target.value)} placeholder="smtp.sendgrid.net" className={inputClass} />
              </FormField>
              <FormField label="Port">
                <input type="number" value={port} onChange={(e) => setPort(Number(e.target.value))} className={inputClass} />
              </FormField>
              <FormField label="Username">
                <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="apikey" className={inputClass} />
              </FormField>
              <FormField label="Password">
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder={query.data?.hasPassword ? "•••••••• (unchanged)" : "Not set"}
                  className={inputClass}
                />
              </FormField>
            </div>
            <div className="grid grid-cols-4 gap-3">
              <div className="col-span-2">
                <FormField label="From address">
                  <input value={fromAddress} onChange={(e) => setFromAddress(e.target.value)} placeholder="alerts@yourdomain.com" className={inputClass} />
                </FormField>
              </div>
              <div className="col-span-2">
                <FormField label="From name">
                  <input value={fromName} onChange={(e) => setFromName(e.target.value)} placeholder="BMS Platform" className={inputClass} />
                </FormField>
              </div>
            </div>

            <Toggle checked={enableSsl} onChange={setEnableSsl} label="Use TLS/SSL" />

            <div className="flex items-center gap-3 pt-1">
              <button disabled={saving} className="text-sm px-4 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
                {saving ? "Saving..." : "Save"}
              </button>
              {saveError && <p className="text-xs text-destructive">{saveError}</p>}
              {saved && !saveError && <p className="text-xs text-success">Saved.</p>}
            </div>
          </form>

          <div className="border-t border-border bg-muted/30 px-4 py-3 space-y-2">
            <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide flex items-center gap-1.5">
              <Send className="h-3 w-3" /> Test
            </h3>
            <div className="flex flex-wrap items-end gap-2">
              <FormField label="Send a test email to">
                <input
                  type="email"
                  value={testEmail}
                  onChange={(e) => setTestEmail(e.target.value)}
                  placeholder="you@example.com"
                  className={`${inputClass} w-64`}
                />
              </FormField>
              <button
                disabled={testing || !testEmail}
                onClick={handleSendTest}
                className="text-sm px-3 py-1.5 rounded-md border border-input bg-background hover:bg-muted text-foreground disabled:opacity-50"
              >
                {testing ? "Sending..." : "Send test email"}
              </button>
            </div>
            {testResult && <p className={`text-xs ${testResult.ok ? "text-success" : "text-destructive"}`}>{testResult.message}</p>}
          </div>
        </div>
      )}
    </section>
  );
}

function TelegramSection() {
  const query = useApi(() => TelegramSettingsApi.get(), []);

  const [botToken, setBotToken] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const [testChatId, setTestChatId] = useState("");
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ ok: boolean; message: string } | null>(null);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setSaveError(null);
    setSaved(false);
    setSaving(true);
    try {
      await TelegramSettingsApi.update(botToken || undefined);
      setBotToken("");
      setSaved(true);
      query.refetch();
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Failed to save Telegram settings");
    } finally {
      setSaving(false);
    }
  }

  async function handleSendTest() {
    setTestResult(null);
    setTesting(true);
    try {
      await TelegramSettingsApi.sendTest(testChatId);
      setTestResult({ ok: true, message: `Sent to chat ${testChatId}.` });
    } catch (err) {
      setTestResult({ ok: false, message: err instanceof ApiError ? err.message : "Failed to send test message" });
    } finally {
      setTesting(false);
    }
  }

  return (
    <section className="settings-notification space-y-2">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-foreground flex items-center gap-1.5">
          <MessageCircle className="h-4 w-4 text-muted-foreground" /> Notifications (Telegram)
        </h2>
        {query.data && <StatusBadge status={query.data.hasBotToken ? "Configured" : "Not configured"} />}
      </div>
      <p className="text-xs text-muted-foreground">
        Free alternative/addition to email — messages a tenant&apos;s Company Admin and assigned Site Admin users on Telegram when a Critical-severity alert
        fires, for any of them who&apos;ve set a Telegram Chat ID on their user account (Users page). Create a bot via @BotFather to get a token.
      </p>

      {query.loading && (
        <div className="bg-card border border-border rounded-lg shadow-sm">
          <LoadingState />
        </div>
      )}
      {query.error && <ErrorState message={query.error} />}

      {!query.loading && !query.error && (
        <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
          <form onSubmit={handleSave} className="p-4 space-y-3">
            <FormField label="Bot token">
              <input
                value={botToken}
                onChange={(e) => setBotToken(e.target.value)}
                placeholder={query.data?.hasBotToken ? "•••••••• (unchanged)" : "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"}
                className={inputClass}
              />
            </FormField>

            <div className="flex items-center gap-3 pt-1">
              <button disabled={saving} className="text-sm px-4 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
                {saving ? "Saving..." : "Save"}
              </button>
              {saveError && <p className="text-xs text-destructive">{saveError}</p>}
              {saved && !saveError && <p className="text-xs text-success">Saved.</p>}
            </div>
          </form>

          <div className="border-t border-border bg-muted/30 px-4 py-3 space-y-2">
            <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide flex items-center gap-1.5">
              <Send className="h-3 w-3" /> Test
            </h3>
            <div className="flex flex-wrap items-end gap-2">
              <FormField label="Send a test message to chat ID">
                <input
                  value={testChatId}
                  onChange={(e) => setTestChatId(e.target.value)}
                  placeholder="123456789"
                  className={`${inputClass} w-64`}
                />
              </FormField>
              <button
                disabled={testing || !testChatId}
                onClick={handleSendTest}
                className="text-sm px-3 py-1.5 rounded-md border border-input bg-background hover:bg-muted text-foreground disabled:opacity-50"
              >
                {testing ? "Sending..." : "Send test message"}
              </button>
            </div>
            {testResult && <p className={`text-xs ${testResult.ok ? "text-success" : "text-destructive"}`}>{testResult.message}</p>}
          </div>
        </div>
      )}
    </section>
  );
}

/** The signed-in user changes their own password; other sessions are signed out. */
function ChangePasswordModal({ onClose, onChanged }: { onClose: () => void; onChanged: () => void }) {
  const { changePassword } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState<{ current?: string; next?: string; confirm?: string }>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaveError(null);
    const found: typeof errors = {};
    if (!current) found.current = "Enter your current password.";
    if (next.length < 8) found.next = "Use at least 8 characters.";
    else if (next === current) found.next = "Choose a password different from the current one.";
    if (confirm !== next) found.confirm = "The passwords don't match.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setSaving(true);
    try {
      await changePassword(current, next);
      onChanged();
      onClose();
    } catch (err) {
      if (err instanceof ApiError && err.errorCode === "CURRENT_PASSWORD_INCORRECT") setErrors({ current: err.message });
      else setSaveError(err instanceof ApiError ? err.message : "Couldn't change the password");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title="Change password" onClose={onClose}>
      <form onSubmit={handleSubmit} noValidate className="space-y-3">
        <FormField label="Current password" error={errors.current}>
          <input type="password" autoComplete="current-password" autoFocus value={current} onChange={(e) => setCurrent(e.target.value)} className={inputClass} />
        </FormField>
        <FormField label="New password" error={errors.next}>
          <input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} className={inputClass} placeholder="At least 8 characters" />
        </FormField>
        <FormField label="Confirm new password" error={errors.confirm}>
          <input type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputClass} />
        </FormField>
        <p className="text-xs text-muted-foreground">Other devices signed in to this account will be signed out.</p>
        {saveError && <p className="text-xs text-destructive">{saveError}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Updating..." : "Update password"}
        </button>
      </form>
    </Modal>
  );
}

export default function SettingsPage() {
  const { user } = useAuth();
  const [changingPassword, setChangingPassword] = useState(false);
  const [passwordChanged, setPasswordChanged] = useState(false);
  const tenantQuery = useApi(() => (user?.tenantId ? TenantsApi.get(user.tenantId) : Promise.resolve(undefined)), [user?.tenantId]);

  return (
    <div className="settings-page space-y-4 sm:space-y-5">
      <div className="flex flex-wrap items-center gap-3 border-b border-border pb-3">
        <div className="flex items-center gap-2 pr-4 border-r border-border"><SettingsIcon className="h-7 w-7 text-primary" /><h1 className="text-2xl font-bold text-foreground">Settings</h1></div>
        <p className="text-sm text-muted-foreground">Manage your profile and notification channels.</p>
      </div>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-foreground flex items-center gap-1.5">
          <User className="h-4 w-4 text-muted-foreground" /> Profile
        </h2>
        <div className="settings-card bg-card border border-border rounded-lg p-4 shadow-sm grid sm:grid-cols-3 gap-x-6 gap-y-3 text-sm">
          <div>
            <span className="text-muted-foreground">Email:</span> <span className="text-foreground">{user?.email}</span>
          </div>
          <div>
            <span className="text-muted-foreground">Role:</span> <span className="text-foreground">{roleLabel(user?.role)}</span>
          </div>
          <div>
            <span className="text-muted-foreground">Company:</span>{" "}
            <span className="text-foreground">{user?.tenantId ? (tenantQuery.data?.name ?? "…") : "All companies"}</span>
          </div>
          <div className="sm:col-span-3 flex flex-wrap items-center gap-3 pt-2 border-t border-border">
            <button
              onClick={() => {
                setPasswordChanged(false);
                setChangingPassword(true);
              }}
              className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md border border-border hover:bg-muted text-foreground"
            >
              <KeyRound className="h-3.5 w-3.5" /> Change password
            </button>
            {passwordChanged && (
              <span className="text-xs text-emerald-600 dark:text-emerald-400">Password updated. You stay signed in here; other devices have been signed out.</span>
            )}
          </div>
        </div>
      </section>

      {changingPassword && <ChangePasswordModal onClose={() => setChangingPassword(false)} onChanged={() => setPasswordChanged(true)} />}

      {user?.role === "SuperAdmin" && <div className="grid gap-4 lg:grid-cols-2"><NotificationsSection /><TelegramSection /></div>}

      <p className="text-xs text-muted-foreground">Per-user notification preferences (e.g. opting out of emails) are a documented follow-up beyond this project&apos;s scope.</p>
    </div>
  );
}
