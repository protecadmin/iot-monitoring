import Link from "next/link";
import type { AlertDto, DeviceDto } from "@/types/api";
import { since } from "./widgets/shared";

type State = "critical" | "warning" | "offline" | "waiting" | "online";

const STYLE: Record<State, { dot: string; chip: string; order: number }> = {
  critical: { dot: "bg-red-500", chip: "border-red-500/60", order: 0 },
  warning: { dot: "bg-amber-500", chip: "border-amber-500/50", order: 1 },
  offline: { dot: "bg-slate-400", chip: "border-border", order: 2 },
  waiting: { dot: "bg-slate-300 dark:bg-slate-600", chip: "border-border border-dashed", order: 3 },
  online: { dot: "bg-emerald-500", chip: "border-border", order: 4 },
};

/** A device's state for the strip: its worst open alert, else whether it is reporting. */
function stateOf(device: DeviceDto, alerts: AlertDto[]): { state: State; note: string } {
  const critical = alerts.filter((a) => a.severity === "Critical").length;
  const warning = alerts.length - critical;
  const count = (n: number) => `${n} alert${n === 1 ? "" : "s"}`;
  if (critical > 0) return { state: "critical", note: count(alerts.length) };
  if (device.status === "Offline") return { state: "offline", note: `offline ${since(device.lastSeenAt)}` };
  if (warning > 0) return { state: "warning", note: count(warning) };
  if (!device.lastSeenAt) return { state: "waiting", note: "no data yet" };
  return { state: "online", note: "OK" };
}

/**
 * Every device at a glance — including ones with nothing on the dashboard
 * cards — worst first, each linking to its page.
 */
export function DeviceStatusStrip({ devices, alerts }: { devices: DeviceDto[]; alerts: AlertDto[] }) {
  if (devices.length === 0) return null;
  const byDevice = new Map<string, AlertDto[]>();
  for (const a of alerts) byDevice.set(a.deviceId, [...(byDevice.get(a.deviceId) ?? []), a]);
  const rows = devices
    .map((d) => ({ device: d, ...stateOf(d, byDevice.get(d.id) ?? []) }))
    .sort((a, b) => STYLE[a.state].order - STYLE[b.state].order || a.device.name.localeCompare(b.device.name));

  return (
    <section className="space-y-1.5">
      <h2 className="text-sm font-semibold text-foreground">Device status</h2>
      <div className="flex flex-wrap gap-1.5">
        {rows.map(({ device, state, note }) => (
          <Link
            key={device.id}
            href={`/devices/${device.id}`}
            title={`${device.name} — ${note}`}
            className={`flex items-center gap-1.5 rounded-md border bg-card px-2 py-1 text-xs shadow-sm hover:border-primary transition-colors ${STYLE[state].chip}`}
          >
            <span className={`h-2 w-2 shrink-0 rounded-full ${STYLE[state].dot}`} />
            <span className="max-w-[12rem] truncate font-medium text-foreground">{device.name}</span>
            <span className="text-muted-foreground">{note}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}
