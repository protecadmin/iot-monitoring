import type { ComponentType } from "react";
import type { DashboardWidget } from "@/types/api";
import type { WidgetItem } from "./useWidgetData";
import { LevelGaugeRow, LiveValueRow, OnOffRow, type RowKind } from "./widgets/rows";
import { SafetyStatusWidget } from "./widgets/SafetyStatusWidget";

/**
 * Widget kind → component. A new kind of meter needs no entry here — its
 * parameters pick one of these kinds on the Device Types page. A new *kind*
 * of widget is one component plus one line here.
 *
 * Safety status gets a card per reading (it carries limits and what to do);
 * live readings are rows of one card per device, so a meter is in one place.
 * Consumption isn't monitoring, so it has no place on the dashboard (Reports).
 */
export const CARD_WIDGETS: Record<"SafetyStatus", ComponentType<{ item: WidgetItem }>> = {
  SafetyStatus: SafetyStatusWidget,
};

export const ROW_WIDGETS: Record<RowKind, ComponentType<{ item: WidgetItem }>> = {
  LiveValue: LiveValueRow,
  LevelGauge: LevelGaugeRow,
  OnOff: OnOffRow,
};

/** Dashboard sections, by purpose, top to bottom. */
export const SECTIONS = [
  { id: "safety", title: "Safety & machine health", kinds: ["SafetyStatus"] },
  { id: "live", title: "Live values & levels", kinds: ["LiveValue", "LevelGauge", "OnOff"] },
] as const satisfies readonly { id: string; title: string; kinds: readonly DashboardWidget[] }[];
