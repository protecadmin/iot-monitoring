export interface TiltAxes { x: string; y: string; z: string | null }
export interface TiltValues { x: number | null; y: number | null; z: number | null }
export interface TiltSettings { closingTime: string; timeZoneId: string; onboardedAt: string }
export interface TiltDailyRow {
  date: string;
  cutoffAt: string;
  readingAt: string | null;
  status: "Pending" | "NoData" | "Complete";
  reading: TiltValues;
  previousDayDifference: TiltValues;
  onboardingDifference: TiltValues;
}
export interface TiltSummary {
  axes: TiltAxes;
  settings: TiltSettings | null;
  baseline: { readingAt: string; values: TiltValues } | null;
  rows: TiltDailyRow[];
}
