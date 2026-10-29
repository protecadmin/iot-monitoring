"use client";

import { useState } from "react";
import { Activity, BookOpen, Clock, RotateCw, Send, type LucideIcon } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { CommandsApi, DevicesApi } from "@/lib/api/resources";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { useCan } from "@/lib/auth/permissions";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { ApiError } from "@/lib/api/client";
import { CommandGuideModal } from "@/components/devices/CommandGuideModal";
import type { DeviceCommandDto } from "@/types/api";

const COMMAND_TYPES: { type: string; icon: LucideIcon; dangerous?: boolean }[] = [
  { type: "Restart", icon: RotateCw, dangerous: true },
  { type: "RequestTelemetry", icon: Activity },
  { type: "ChangeReportingInterval", icon: Clock },
  { type: "SynchronizeTime", icon: Clock },
];

/** These match the backend's CommandType enum names exactly (PascalCase, no spaces) — spaced out here for display only. */
function displayLabel(commandType: string): string {
  return commandType.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function CommandsTab({ deviceId }: { deviceId: string }) {
  const query = useApi(() => CommandsApi.list(deviceId), [deviceId]);
  const deviceQuery = useApi(() => DevicesApi.get(deviceId), [deviceId]);
  const canSend = useCan()("commands.send");
  const [sending, setSending] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [settingInterval, setSettingInterval] = useState(false);
  const [showGuide, setShowGuide] = useState(false);
  const [live, setLive] = useState<DeviceCommandDto[] | undefined>(undefined);
  const commands = live ?? query.data;

  useRealtime({
    CommandStatusChanged: (payload) => {
      const c = payload as DeviceCommandDto;
      if (c.deviceId !== deviceId) return;
      setLive((prev) => {
        const base = prev ?? query.data ?? [];
        const exists = base.some((x) => x.id === c.id);
        return exists ? base.map((x) => (x.id === c.id ? c : x)) : [c, ...base];
      });
    },
  });

  async function send(type: string, payloadJson?: string) {
    setSending(type);
    try {
      await CommandsApi.create(deviceId, type, payloadJson);
      query.refetch();
      setLive(undefined);
    } finally {
      setSending(null);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        {canSend && COMMAND_TYPES.map(({ type, icon: Icon, dangerous }) => (
          <button
            key={type}
            disabled={sending === type}
            onClick={() => {
              if (type === "ChangeReportingInterval") setSettingInterval(true);
              else if (dangerous) setConfirming(type);
              else send(type);
            }}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          >
            <Icon className="h-3.5 w-3.5" />
            {sending === type ? "Sending..." : displayLabel(type)}
          </button>
        ))}
        <button
          onClick={() => setShowGuide(true)}
          disabled={!deviceQuery.data}
          title="What each command sends over MQTT and how the device must reply"
          className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md border border-input text-foreground hover:bg-muted disabled:opacity-50 sm:ml-auto"
        >
          <BookOpen className="h-3.5 w-3.5" />
          Command guide
        </button>
      </div>

      {query.loading && <LoadingState />}
      {query.error && <ErrorState message={query.error} />}
      {!query.loading && !query.error && (!commands || commands.length === 0) && (
        <EmptyState message="No commands sent yet." hint="Commands sent to this device and their delivery status will appear here." icon={Send} />
      )}

      {commands && commands.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-x-auto shadow-sm">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide font-medium">
              <tr>
                <th className="text-left px-4 py-2">Type</th>
                <th className="text-left px-4 py-2">Status</th>
                <th className="text-left px-4 py-2">Created</th>
                <th className="text-left px-4 py-2">Error</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {commands.map((c) => (
                <tr key={c.id}>
                  <td className="px-4 py-2 text-foreground">{displayLabel(c.type)}</td>
                  <td className="px-4 py-2">
                    <StatusBadge status={c.status} />
                  </td>
                  <td className="px-4 py-2 text-muted-foreground text-xs">{new Date(c.createdAt).toLocaleString()}</td>
                  <td className="px-4 py-2 text-destructive text-xs">{c.error ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {confirming && (
        <ConfirmModal
          title={`${displayLabel(confirming)} controller`}
          confirmLabel={`${displayLabel(confirming)}`}
          message={
            <>
              This sends a real command over MQTT to the device below. It will interrupt normal operation until it reconnects.
              <br />
              <br />
              <span className="text-foreground font-medium">Target:</span> {deviceQuery.data?.name ?? deviceId}
              <br />
              <span className="text-foreground font-medium">Device code:</span> {deviceQuery.data?.deviceCode ?? "—"}
            </>
          }
          onClose={() => setConfirming(null)}
          onConfirm={() => send(confirming)}
        />
      )}

      {showGuide && deviceQuery.data && (
        <CommandGuideModal
          device={deviceQuery.data}
          // Newest command still awaiting its ack, so the guide's example reply completes a real command.
          pendingCommandId={commands?.find((c) => c.status === "Sent")?.id}
          onClose={() => setShowGuide(false)}
        />
      )}

      {settingInterval && (
        <ChangeReportingIntervalModal
          onClose={() => setSettingInterval(false)}
          onSubmit={async (seconds) => {
            // The device simulator's HandleCommandAsync expects `payload` as a
            // JSON string it int.TryParse()s — not a nested object — since
            // MqttDeviceCommandDispatcher forwards DeviceCommand.PayloadJson
            // through untouched. So this must stay a bare digit string, e.g. "30".
            await send("ChangeReportingInterval", String(seconds));
          }}
        />
      )}
    </div>
  );
}

function ChangeReportingIntervalModal({ onClose, onSubmit }: { onClose: () => void; onSubmit: (seconds: number) => Promise<void> }) {
  const [seconds, setSeconds] = useState(30);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      await onSubmit(seconds);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to send command");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title="Change reporting interval" onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="New interval (seconds)">
          <input
            required
            type="number"
            min={1}
            value={seconds}
            onChange={(e) => setSeconds(Number(e.target.value))}
            className={inputClass}
          />
        </FormField>
        <p className="text-xs text-muted-foreground">
          Sent as a real command over MQTT — the device applies it as soon as it processes the message.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Sending..." : "Send command"}
        </button>
      </form>
    </Modal>
  );
}
