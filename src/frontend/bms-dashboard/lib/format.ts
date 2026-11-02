/**
 * Calculated consumption per reading is often a few thousandths of a unit, so
 * two decimals would show a steady stream of "0.00". Precision scales with
 * magnitude instead: small values keep enough digits to be meaningful.
 */
export function measurementDecimals(parameterKey: string): number {
  return /^(?:[xyz]Deg|a[xyz]G)$/i.test(parameterKey) ? 4 : 2;
}

export function formatMeasurement(value: number, parameterKey?: string): string {
  if (parameterKey) return value.toFixed(measurementDecimals(parameterKey));
  const abs = Math.abs(value);
  if (abs === 0) return "0";
  if (abs >= 100) return value.toFixed(2);
  if (abs >= 1) return value.toFixed(3);
  return value.toFixed(4);
}

export function humanizeKey(key: string): string {
  return key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase());
}

/** Display only; daily tilt differences are calculated by the backend. */
export function formatTiltNumber(value: number | null | undefined, signed = false): string {
  if (value == null || !Number.isFinite(value)) return "—";
  const rounded = Number(value.toFixed(4));
  return `${signed && rounded > 0 ? "+" : ""}${rounded.toFixed(4)}`;
}
