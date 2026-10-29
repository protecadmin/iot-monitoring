import Link from "next/link";
import { Bell } from "lucide-react";
import { EmptyState, StatusBadge } from "@/components/ui/States";
import type { AlertDto, DeviceDto } from "@/types/api";
import { since } from "./widgets/shared";

const SEVERITY_ORDER: Record<string, number> = { Critical: 0, Warning: 1, Info: 2 };
const MAX_ROWS = 8;

/**
 * What needs attention now: every unresolved alert, critical first and
 * newest first, with the device it is on and how long it has been open.
 * Acknowledging marks it as seen; it stays until the reading recovers.
 */
export function LiveAlerts({
  alerts,
  devices,
  canHandle,
  onAcknowledge,
}: {
  alerts: AlertDto[];
  devices: Map<string, DeviceDto>;
  canHandle: boolean;
  onAcknowledge: (alert: AlertDto) => void;
}) {
  const rows = [...alerts].sort(
    (a, b) => (SEVERITY_ORDER[a.severity] ?? 3) - (SEVERITY_ORDER[b.severity] ?? 3) || b.triggeredAt.localeCompare(a.triggeredAt),
  );

  return (
    <section className="bg-card border border-border rounded-lg p-3 shadow-sm">
      <div className="flex items-center justify-between mb-1">
        <h2 className="text-sm font-semibold text-foreground">Live alerts</h2>
        {rows.length > 0 && (
          <Link href="/alerts" className="text-xs text-primary hover:underline">
            All {rows.length} →
          </Link>
        )}
      </div>
      {rows.length === 0 ? (
        <EmptyState message="No active alerts" hint="Every device is within its alert rules." icon={Bell} />
      ) : (
        <ul className="divide-y divide-border">
          {rows.slice(0, MAX_ROWS).map((a) => {
            const device = devices.get(a.deviceId);
            const acknowledged = a.status === "Acknowledged";
            return (
              <li key={a.id} className={`py-1.5 text-xs flex items-center gap-3 ${acknowledged ? "opacity-70" : ""}`}>
                <StatusBadge status={a.severity} />
                <div className="min-w-0 flex-1 truncate" title={a.message}>
                  <span className="font-medium text-foreground">{device?.name ?? "Unknown device"}</span>
                  <span className="text-muted-foreground"> · {a.message}</span>
                </div>
                {acknowledged && <span className="shrink-0 text-[11px] text-muted-foreground">Acknowledged</span>}
                <span className="shrink-0 w-14 text-right text-muted-foreground tabular-nums" title={new Date(a.triggeredAt).toLocaleString()}>
                  {since(a.triggeredAt)}
                </span>
                {canHandle && !acknowledged && (
                  <button onClick={() => onAcknowledge(a)} className="shrink-0 text-primary hover:underline">
                    Ack
                  </button>
                )}
                <Link href={`/devices/${a.deviceId}`} className="shrink-0 text-primary hover:underline">
                  View
                </Link>
              </li>
            );
          })}
        </ul>
      )}
      {rows.length > MAX_ROWS && <p className="pt-1 text-[11px] text-muted-foreground">+{rows.length - MAX_ROWS} more on the Alerts page</p>}
    </section>
  );
}
