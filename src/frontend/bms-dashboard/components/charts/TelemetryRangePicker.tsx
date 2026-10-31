import type { TelemetryRange } from "@/types/api";

const RANGES: { value: TelemetryRange; label: string }[] = [
  { value: "Live", label: "Live" },
  { value: "OneHour", label: "1 Hour" },
  { value: "SixHours", label: "6 Hours" },
  { value: "TwentyFourHours", label: "24 Hours" },
  { value: "SevenDays", label: "7 Days" },
];

export function TelemetryRangePicker({ range, onChange }: { range: TelemetryRange; onChange: (range: TelemetryRange) => void }) {
  return (
    <div className="flex gap-1 mb-3">
      {RANGES.map((r) => (
        <button
          key={r.value}
          type="button"
          aria-pressed={range === r.value}
          onClick={() => onChange(r.value)}
          className={`text-xs px-2 py-1 rounded ${range === r.value ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground hover:bg-muted"}`}
        >
          {r.label}
        </button>
      ))}
    </div>
  );
}
