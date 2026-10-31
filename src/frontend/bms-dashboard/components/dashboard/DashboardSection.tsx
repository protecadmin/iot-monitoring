/** A titled grid of widgets; renders nothing when it has none, so a company never sees an empty section. */
export function DashboardSection({ title, summary, children, count, accent = "default" }: { title: string; summary?: React.ReactNode; children: React.ReactNode; count: number; accent?: "noise" | "vibration" | "heat" | "default" }) {
  if (count === 0) return null;
  return (
    <section data-accent={accent} className="equipment-group space-y-1.5 dark:min-w-0 dark:space-y-3">
      <div className="equipment-heading flex flex-wrap items-baseline gap-x-3 gap-y-1 dark:flex-col dark:items-start dark:border-l-2 dark:border-primary/60 dark:pl-3">
        <h2 className="text-sm font-semibold text-foreground">{title}</h2>
        {summary && <span className="text-xs text-muted-foreground">{summary}</span>}
      </div>
      <div className="equipment-cards grid grid-cols-[repeat(auto-fit,minmax(min(100%,15rem),1fr))] gap-2.5">{children}</div>
    </section>
  );
}
