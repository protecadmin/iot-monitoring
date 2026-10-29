import Link from "next/link";
import type { DeviceDto } from "@/types/api";
import type { Health, ResolvedTwin } from "./types";
import { HEALTH_STYLE, formatReading } from "./limits";

/** One equipment on the Digital Twin overview: small picture, state and the headline reading. */
export function TwinTile({ device, twin, value, health }: { device: DeviceDto; twin: ResolvedTwin; value: number | undefined; health: Health }) {
  const style = HEALTH_STYLE[health];
  const { Illustration } = twin.template;
  const primary = twin.profile.primary;
  const running = health !== "offline" && health !== "unknown";

  return (
    <Link
      href={`/digital-twin?device=${device.id}`}
      className={`group block bg-card rounded-xl border border-border shadow-sm p-4 ring-1 ${style.ring} hover:shadow-md focus-visible:outline-2 focus-visible:outline-primary transition-shadow`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-sm font-semibold text-foreground truncate group-hover:text-primary">{device.name}</div>
          <div className="text-xs text-muted-foreground truncate">{twin.assetName}</div>
        </div>
        <span className={`shrink-0 inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-semibold ${style.bg} ${style.text}`}>
          <span className="h-1.5 w-1.5 rounded-full" style={{ background: style.hex }} />
          {health === "normal" ? "Online" : style.label}
        </span>
      </div>
      <div className="my-2 rounded-lg bg-gradient-to-b from-muted/40 to-card px-2">
        <Illustration health={health} running={running} />
      </div>
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-xs text-muted-foreground">{primary.label}</span>
        <span className="tabular-nums">
          <span className="text-2xl font-bold text-foreground">{formatReading(value, primary.decimals)}</span>{" "}
          <span className="text-xs text-muted-foreground">{primary.unit}</span>
        </span>
      </div>
    </Link>
  );
}
