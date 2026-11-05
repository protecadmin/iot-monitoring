import { AlertTriangle, Inbox, Loader2, type LucideIcon } from "lucide-react";

export function LoadingState({ label = "Loading..." }: { label?: string }) {
  return (
    <div role="status" className="py-16 flex flex-col items-center justify-center gap-3 text-sm text-muted-foreground">
      <Loader2 className="h-6 w-6 animate-spin text-primary" aria-hidden="true" />
      {label}
    </div>
  );
}

export function ErrorState({ message }: { message: string }) {
  return (
    <div role="alert" className="py-10 px-4 flex flex-col items-center justify-center gap-3 text-center text-sm text-destructive bg-destructive/5 border border-destructive/20 rounded-xl">
      <AlertTriangle className="h-5 w-5" />
      {message}
    </div>
  );
}

export function EmptyState({ message, hint, icon: Icon = Inbox }: { message: string; hint?: string; icon?: LucideIcon }) {
  return (
    <div className="py-14 px-5 flex flex-col items-center justify-center gap-3 text-center">
      <span className="rounded-2xl bg-muted p-4"><Icon className="h-7 w-7 text-primary" aria-hidden="true" /></span>
      <p className="text-sm font-medium text-foreground">{message}</p>
      {hint && <p className="text-xs text-muted-foreground/70 max-w-sm">{hint}</p>}
    </div>
  );
}

export function TableSkeleton({ rows = 5, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <div className="animate-pulse">
      {Array.from({ length: rows }).map((_, r) => (
        <div key={r} className="flex gap-4 px-4 py-3 border-b border-border last:border-0">
          {Array.from({ length: cols }).map((_, c) => (
            <div key={c} className="h-3.5 bg-muted rounded flex-1" style={{ maxWidth: c === 0 ? "40%" : undefined }} />
          ))}
        </div>
      ))}
    </div>
  );
}

const TONES = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  critical: "text-destructive",
} as const;

export function StatCard({
  label,
  value,
  tone,
  icon: Icon,
}: {
  label: string;
  value: string | number;
  tone?: keyof typeof TONES;
  icon?: LucideIcon;
}) {
  return (
    <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <div className="text-xs font-medium text-muted-foreground uppercase tracking-wide">{label}</div>
        {Icon && <Icon className={`h-4 w-4 ${TONES[tone ?? "default"]}`} />}
      </div>
      <div className={`mt-1.5 text-2xl font-semibold tabular-nums ${TONES[tone ?? "default"]}`}>{value}</div>
    </div>
  );
}

type BadgeTone = "success" | "warning" | "destructive" | "info" | "neutral";

const STATUS_TONE: Record<string, BadgeTone> = {
  // Device lifecycle
  Online: "success",
  Connected: "success",
  Disconnected: "neutral",
  Healthy: "success",
  Live: "success",
  Waiting: "warning",
  Offline: "neutral",
  Active: "success",
  Inactive: "neutral",
  Registered: "neutral",
  Provisioned: "info",
  Warning: "warning",
  Error: "destructive",
  Maintenance: "info",
  Decommissioned: "neutral",
  // Alerts (both severity and status share this lookup)
  Critical: "destructive",
  Info: "info",
  Open: "destructive",
  Acknowledged: "info",
  Resolved: "success",
  // Commands / firmware deployments
  Pending: "warning",
  Sent: "info",
  Downloading: "info",
  Verifying: "info",
  Installing: "info",
  Rebooting: "info",
  WaitingForDevice: "warning",
  InProgress: "info",
  Completed: "success",
  Success: "success",
  Failed: "destructive",
  Timeout: "destructive",
  // Settings
  Configured: "success",
  "Not configured": "neutral",
};

const BADGE_CLASSES: Record<BadgeTone, string> = {
  success: "bg-success-muted text-success-muted-foreground",
  warning: "bg-warning-muted text-warning-muted-foreground",
  destructive: "bg-destructive/10 text-destructive",
  info: "bg-info-muted text-info-muted-foreground",
  neutral: "bg-muted text-muted-foreground",
};

const DOT_CLASSES: Record<BadgeTone, string> = {
  success: "bg-success",
  warning: "bg-warning",
  destructive: "bg-destructive",
  info: "bg-info",
  neutral: "bg-muted-foreground",
};

/** Splits "NoTelemetry" -> "No Telemetry" for any PascalCase enum value rendered as a label. */
function spaceOutPascalCase(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}

export function StatusBadge({ status }: { status: string }) {
  const tone = STATUS_TONE[status] ?? "neutral";
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-semibold whitespace-nowrap ${BADGE_CLASSES[tone]}`}>
      <span aria-hidden="true" className={`h-1.5 w-1.5 rounded-full ${DOT_CLASSES[tone]}`} />
      {spaceOutPascalCase(status)}
    </span>
  );
}

export function LiveIndicator({ lastUpdated }: { lastUpdated?: Date | null }) {
  return (
    <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
      <span className="relative flex h-2 w-2">
        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-success opacity-75" />
        <span className="relative inline-flex rounded-full h-2 w-2 bg-success" />
      </span>
      Live
      {lastUpdated && <span className="text-muted-foreground/70">· {lastUpdated.toLocaleTimeString()}</span>}
    </div>
  );
}
