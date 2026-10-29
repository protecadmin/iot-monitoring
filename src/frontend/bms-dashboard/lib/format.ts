/**
 * Calculated consumption per reading is often a few thousandths of a unit, so
 * two decimals would show a steady stream of "0.00". Precision scales with
 * magnitude instead: small values keep enough digits to be meaningful.
 */
export function formatMeasurement(value: number): string {
  const abs = Math.abs(value);
  if (abs === 0) return "0";
  if (abs >= 100) return value.toFixed(2);
  if (abs >= 1) return value.toFixed(3);
  return value.toFixed(4);
}

export function humanizeKey(key: string): string {
  return key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase());
}
