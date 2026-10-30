/** A titled grid of widgets; renders nothing when it has none, so a company never sees an empty section. */
export function DashboardSection({ title, summary, children, count }: { title: string; summary?: React.ReactNode; children: React.ReactNode; count: number }) {
  if (count === 0) return null;
  return (
    <section className="space-y-1.5">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h2 className="text-sm font-semibold text-foreground">{title}</h2>
        {summary && <span className="text-xs text-muted-foreground">{summary}</span>}
      </div>
      <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,15rem),1fr))] gap-2.5">{children}</div>
    </section>
  );
}
