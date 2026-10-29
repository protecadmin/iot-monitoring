"use client";

import { useState } from "react";
import { useApi } from "@/hooks/useApi";
import { RawMessagesApi } from "@/lib/api/resources";
import { useCan } from "@/lib/auth/permissions";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";

function decodeBase64Preview(base64: string): string {
  try {
    const binary = atob(base64);
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    const text = new TextDecoder("utf-8", { fatal: false }).decode(bytes);
    // Printable enough to show as text (JSON/proprietary); otherwise show hex.
    if (/^[\x09\x0A\x0D\x20-\x7E]*$/.test(text)) return text;
    return Array.from(bytes).map((b) => b.toString(16).padStart(2, "0")).join(" ");
  } catch {
    return "(unable to decode)";
  }
}

export function RawMessagesTab({ deviceId }: { deviceId: string }) {
  const query = useApi(() => RawMessagesApi.list(deviceId), [deviceId]);
  const canReplay = useCan()("rawMessages.replay");
  const [replaying, setReplaying] = useState<string | null>(null);

  if (query.loading) return <LoadingState />;
  if (query.error) return <ErrorState message={query.error} />;
  if (!query.data || query.data.items.length === 0) return <EmptyState message="No raw messages recorded yet." />;

  async function replay(id: string) {
    setReplaying(id);
    try {
      await RawMessagesApi.replay(id);
      query.refetch();
    } finally {
      setReplaying(null);
    }
  }

  return (
    <div className="space-y-2">
      {query.data.items.map((m) => (
        <div key={m.id} className="bg-card border border-border rounded-lg p-3 shadow-sm">
          <div className="flex items-center justify-between">
            <div className="text-xs text-muted-foreground">
              {new Date(m.receivedAt).toLocaleString()} · {m.transport} / {m.protocol} / {m.payloadFormat}
            </div>
            <div className="flex items-center gap-2">
              <StatusBadge status={m.processingStatus} />
              {canReplay && (
                <button
                  disabled={replaying === m.id}
                  onClick={() => replay(m.id)}
                  className="text-xs text-primary hover:underline disabled:opacity-50"
                >
                  {replaying === m.id ? "Replaying..." : "Replay"}
                </button>
              )}
            </div>
          </div>
          {m.processingError && <div className="text-xs text-destructive mt-1">{m.processingError}</div>}
          <pre className="mt-2 text-xs bg-muted rounded p-2 overflow-x-auto">{decodeBase64Preview(m.rawPayloadBase64)}</pre>
        </div>
      ))}
    </div>
  );
}
