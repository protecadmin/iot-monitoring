import type { Level } from "@/components/digital-twin/types";
import type { DeviceDto } from "@/types/api";
import { ROW_WIDGETS } from "../registry";
import type { WidgetItem } from "../useWidgetData";
import { rowLevel, type RowKind } from "./rows";
import { DeviceCard, LEVEL_ORDER } from "./shared";

/** At most this many readings on a card; the rest are on the device page. */
const MAX_ROWS = 4;

/** Worst status among a device's readings, or null when none of them has one. */
export function worstLevel(items: WidgetItem[]): Level | null {
  const levels = items.map(rowLevel).filter((l): l is Level => l != null);
  if (levels.length === 0) return null;
  return levels.sort((a, b) => LEVEL_ORDER[a] - LEVEL_ORDER[b])[0];
}

/**
 * One card per device for its live readings: a row each, worst first, so a
 * meter is found in one place however many of its readings are on the dashboard.
 */
export function DeviceReadingsCard({ device, typeName, items }: { device: DeviceDto; typeName: string; items: WidgetItem[] }) {
  const rows = [...items].sort((a, b) => LEVEL_ORDER[rowLevel(a) ?? "normal"] - LEVEL_ORDER[rowLevel(b) ?? "normal"]);
  const shown = rows.slice(0, MAX_ROWS);
  const hidden = rows.length - shown.length;
  return (
    <DeviceCard device={device} subtitle={typeName} level={worstLevel(items)}>
      <div className="space-y-1">
        {shown.map((item) => {
          const Row = ROW_WIDGETS[item.kind as RowKind];
          return <Row key={item.parameter.key} item={item} />;
        })}
      </div>
      {hidden > 0 && <p className="text-[11px] text-primary">+{hidden} more on the device page →</p>}
    </DeviceCard>
  );
}
