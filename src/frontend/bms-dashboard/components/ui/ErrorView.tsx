import Link from "next/link";
import { AlertTriangle, FileQuestion } from "lucide-react";

/**
 * The friendly face of a crash or a wrong link: what happened in plain words,
 * a way to try again, and a way back. Technical detail stays out — only the
 * error's reference id, which matches the server log.
 */
export function ErrorView({
  kind,
  digest,
  onRetry,
}: {
  kind: "error" | "not-found";
  digest?: string;
  onRetry?: () => void;
}) {
  const notFound = kind === "not-found";
  const Icon = notFound ? FileQuestion : AlertTriangle;
  return (
    <div className="flex flex-1 items-center justify-center px-4 py-16">
      <div className="max-w-md text-center space-y-3">
        <Icon className={`h-10 w-10 mx-auto ${notFound ? "text-muted-foreground" : "text-amber-500"}`} />
        <h1 className="text-lg font-semibold text-foreground">{notFound ? "Page not found" : "Something went wrong"}</h1>
        <p className="text-sm text-muted-foreground">
          {notFound
            ? "This page doesn't exist or has moved. Check the address, or go back to the dashboard."
            : "This page couldn't load. It's usually temporary — try again, and if it keeps happening, contact your administrator."}
        </p>
        <div className="flex flex-wrap justify-center gap-2 pt-1">
          {onRetry && (
            <button onClick={onRetry} className="text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90">
              Try again
            </button>
          )}
          <Link
            href="/dashboard"
            className={`text-sm px-3 py-1.5 rounded-md ${onRetry ? "border border-border text-foreground hover:bg-muted" : "bg-primary text-primary-foreground hover:bg-primary/90"}`}
          >
            Go to dashboard
          </Link>
        </div>
        {digest && <p className="text-[11px] text-muted-foreground pt-2">Reference: {digest}</p>}
      </div>
    </div>
  );
}
