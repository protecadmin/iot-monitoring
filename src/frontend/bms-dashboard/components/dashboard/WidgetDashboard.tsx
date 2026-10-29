"use client";

import { ErrorState } from "@/components/ui/States";
import { DashboardSection } from "./DashboardSection";
import { CARD_WIDGETS, SECTIONS } from "./registry";
import { useWidgetData } from "./useWidgetData";
import { DeviceReadingsCard, worstLevel } from "./widgets/DeviceReadingsCard";
import { LEVEL_ORDER, isOffline } from "./widgets/shared";

/**
 * The client-facing dashboard sections. Which widget a device gets comes from
 * its device type's parameters ("Show on dashboard as" on Device Types), so a
 * new kind of meter appears here without code. Safety readings get a card
 * each and live readings are grouped into one card per device, worst first.
 */
export function WidgetDashboard({ tenantId, siteId }: { tenantId: string | undefined; siteId?: string }) {
  const { items, error } = useWidgetData(tenantId, siteId || undefined);

  if (error) return <ErrorState message={error} />;
  if (items.length === 0) return null;

  return (
    <div className="space-y-4">
      {SECTIONS.map((section) => {
        const kinds: readonly string[] = section.kinds;
        const cards = items
          .filter((i) => kinds.includes(i.kind))
          .sort(
            (a, b) =>
              Number(isOffline(a.device)) - Number(isOffline(b.device)) ||
              LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level] ||
              a.device.name.localeCompare(b.device.name),
          );

        // At a glance: how many cards need attention.
        const live = cards.filter((c) => !isOffline(c.device));
        const danger = live.filter((c) => c.level === "critical").length;
        const caution = live.filter((c) => c.level === "warning").length;
        const good = live.filter((c) => c.level === "normal").length;
        const summary =
          section.id === "safety" ? (
            <>
              {danger > 0 && <span className="text-red-700 dark:text-red-400">{danger} danger · </span>}
              {caution > 0 && <span className="text-amber-700 dark:text-amber-400">{caution} caution · </span>}
              <span>{good} good</span>
              {cards.length > live.length && <span> · {cards.length - live.length} offline</span>}
              <span className="hidden sm:inline"> — limits from each device&apos;s alert rules</span>
            </>
          ) : undefined;

        if (section.id === "live") {
          // One card per device: its readings are rows, so a meter is in one place.
          const groups = new Map<string, typeof cards>();
          for (const c of cards) groups.set(c.device.id, [...(groups.get(c.device.id) ?? []), c]);
          const devices = [...groups.values()].sort(
            (a, b) =>
              Number(isOffline(a[0].device)) - Number(isOffline(b[0].device)) ||
              LEVEL_ORDER[worstLevel(a) ?? "normal"] - LEVEL_ORDER[worstLevel(b) ?? "normal"] ||
              a[0].device.name.localeCompare(b[0].device.name),
          );
          return (
            <DashboardSection key={section.id} title={section.title} count={devices.length}>
              {devices.map((g) => (
                <DeviceReadingsCard key={g[0].device.id} device={g[0].device} typeName={g[0].typeName} items={g} />
              ))}
            </DashboardSection>
          );
        }

        return (
          <DashboardSection key={section.id} title={section.title} summary={summary} count={cards.length}>
            {cards.map((item) => {
              const Widget = CARD_WIDGETS.SafetyStatus;
              return <Widget key={`${item.device.id}:${item.parameter.key}`} item={item} />;
            })}
          </DashboardSection>
        );
      })}
    </div>
  );
}
