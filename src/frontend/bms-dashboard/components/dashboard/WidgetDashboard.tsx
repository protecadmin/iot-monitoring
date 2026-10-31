"use client";

import { ErrorState } from "@/components/ui/States";
import { DashboardSection } from "./DashboardSection";
import { CARD_WIDGETS, SECTIONS } from "./registry";
import { useWidgetData, type WidgetItem } from "./useWidgetData";
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

  const byType = new Map<string, WidgetItem[]>();
  for (const item of items) {
    const group = byType.get(item.device.deviceTypeId) ?? [];
    group.push(item);
    byType.set(item.device.deviceTypeId, group);
  }
  const typeGroups = [...byType.entries()].sort(([, a], [, b]) => a[0].typeName.localeCompare(b[0].typeName));

  return (
    <div className="space-y-4 dark:grid dark:grid-cols-1 dark:gap-5 dark:space-y-0 dark:lg:grid-cols-2 dark:2xl:grid-cols-3">
      <div className="dark:col-span-full">
        <h2 className="text-base font-semibold text-foreground">Equipment by type</h2>
        <p className="text-xs text-muted-foreground">Select a device to see its readings, history and alerts.</p>
      </div>
      {typeGroups.flatMap(([typeId, typeItems]) => SECTIONS.map((section) => {
        const kinds: readonly string[] = section.kinds;
        const cards = typeItems
          .filter((i) => kinds.includes(i.kind))
          .sort(
            (a, b) =>
              Number(isOffline(a.device)) - Number(isOffline(b.device)) ||
              LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level] ||
              a.device.name.localeCompare(b.device.name),
          );

        // Count devices once, using the worst status among their readings.
        const deviceGroups = new Map<string, WidgetItem[]>();
        for (const card of cards) {
          const group = deviceGroups.get(card.device.id) ?? [];
          group.push(card);
          deviceGroups.set(card.device.id, group);
        }
        const statusCounts = { normal: 0, warning: 0, critical: 0, unknown: 0, offline: 0 };
        for (const readings of deviceGroups.values()) {
          if (isOffline(readings[0].device)) {
            statusCounts.offline++;
          } else {
            const level = [...readings].sort((a, b) => LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level])[0].level;
            statusCounts[level]++;
          }
        }
        const summary = (
          <span className="flex flex-wrap gap-x-3 gap-y-1">
            <span>{deviceGroups.size} {deviceGroups.size === 1 ? "device" : "devices"}</span>
            {statusCounts.critical > 0 && <span className="text-red-700 dark:text-red-400">{statusCounts.critical} need urgent attention</span>}
            {statusCounts.warning > 0 && <span className="text-amber-700 dark:text-amber-400">{statusCounts.warning} need checking</span>}
            {statusCounts.normal > 0 && <span className="text-emerald-700 dark:text-emerald-400">{statusCounts.normal} within limits</span>}
            {statusCounts.offline > 0 && <span>{statusCounts.offline} offline</span>}
            {statusCounts.unknown > 0 && <span>{statusCounts.unknown} awaiting readings</span>}
          </span>
        );
        const typeName = typeItems[0].typeName || "Other devices";
        const title = section.id === "safety" ? typeName : `${typeName} - Live readings`;
        const typeLabel = typeName.toLowerCase();
        const accent = typeLabel.includes("noise") ? "noise"
          : typeLabel.includes("vibration") ? "vibration"
          : /wbgt|heat/.test(typeLabel) ? "heat" : "default";

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
            <DashboardSection key={`${typeId}:${section.id}`} title={title} accent={accent} summary={summary} count={devices.length}>
              {devices.map((g) => (
                <DeviceReadingsCard key={g[0].device.id} device={g[0].device} typeName={g[0].typeName} items={g} />
              ))}
            </DashboardSection>
          );
        }

        return (
          <DashboardSection key={`${typeId}:${section.id}`} title={title} accent={accent} summary={summary} count={cards.length}>
            {cards.map((item) => {
              const Widget = CARD_WIDGETS.SafetyStatus;
              return <Widget key={`${item.device.id}:${item.parameter.key}`} item={item} />;
            })}
          </DashboardSection>
        );
      }))}
    </div>
  );
}
