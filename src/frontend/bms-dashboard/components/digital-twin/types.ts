import type { ComponentType } from "react";

/** How a value compares with its limits. "unknown" = no reading yet, or no limits for it. */
export type Level = "normal" | "warning" | "critical" | "unknown";

/** Overall state shown on the twin: the worst metric level, or Offline when the device isn't reporting. */
export type Health = Level | "offline";

export interface Limits {
  warning: number;
  critical: number;
}

/**
 * Limits on both sides of a reading. `high` comes from "greater than" rules
 * (warning ≤ critical); `low` from "less than" rules (warning ≥ critical —
 * e.g. caution under 210 V, danger under 200 V). Either side may be absent.
 */
export interface Range {
  high?: Limits;
  low?: Limits;
}

export interface TwinMetric {
  /** Telemetry parameter key, e.g. "vibration" — must match the Device Type parameter. */
  key: string;
  label: string;
  unit: string;
  decimals?: number;
  /**
   * Fallback limits when the tenant has no enabled "greater than" alert rule
   * for this metric. Real limits always come from Alert Rules, so the twin and
   * the alerts it shows never disagree.
   */
  defaultLimits?: Limits;
}

/**
 * What a kind of sensor measures, independent of the equipment it is fitted
 * to. A vibration sensor reads the same values on a compressor or a pump;
 * only the picture (the template) changes.
 */
export interface MetricProfile {
  id: string;
  /** Device Type names (as on the Device Types page) that report these readings. */
  deviceTypes: string[];
  /** The headline reading: drives health, the severity scale and the trend line. */
  primary: TwinMetric;
  secondary: TwinMetric[];
}

/** Props every illustration receives. */
export interface IllustrationProps {
  health: Health;
  /** The device is Online and sending — drive motion (spinning fan, waves) from this. */
  running: boolean;
}

/** A place on the equipment where the sensor can be fitted, in the illustration's viewBox coordinates. */
export interface MountPoint {
  id: string;
  label: string;
  x: number;
  y: number;
}

/**
 * One picture of equipment a sensor can be fitted to. Each template lives in
 * its own folder under designs/ and is listed in registry.ts.
 */
export interface TwinTemplate {
  /** Stored on the device — keep it stable once in use. Lowercase letters, digits and dashes. */
  id: string;
  name: string;
  description: string;
  /** Which MetricProfile's devices can use it. */
  profile: string;
  /** Default equipment name shown on the twin, e.g. "Air Compressor". */
  assetLabel: string;
  Illustration: ComponentType<IllustrationProps>;
  viewBox: { width: number; height: number };
  /** Where the sensor can sit; the first is the default. Ids are stored on the device. */
  mountPoints: MountPoint[];
}

/** A device's twin after applying its saved choices (or the defaults). */
export interface ResolvedTwin {
  profile: MetricProfile;
  template: TwinTemplate;
  mountPoint: MountPoint;
  assetName: string;
  /** The device uses defaults — nobody has chosen a template for it yet. */
  isDefault: boolean;
}
