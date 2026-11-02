import { FormField, inputClass } from "@/components/ui/Modal";

export function TiltScheduleFields({ closingTime, timeZoneId, onTimeChange, onZoneChange, disabled = false }: {
  closingTime: string; timeZoneId: string; onTimeChange: (value: string) => void; onZoneChange: (value: string) => void; disabled?: boolean;
}) {
  return (
    <fieldset disabled={disabled} className="space-y-3 rounded-lg border border-border bg-secondary/30 p-4">
      <legend className="px-1 text-sm font-semibold text-foreground">Daily tilt reading</legend>
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField label="Daily closing time">
          <input type="time" required value={closingTime} onChange={(e) => onTimeChange(e.target.value)} className={inputClass} aria-label="Daily closing time" />
        </FormField>
        <FormField label="Device timezone">
          <input required maxLength={100} list="tilt-timezones" value={timeZoneId} onChange={(e) => onZoneChange(e.target.value)} className={inputClass} aria-label="Device timezone" />
          <datalist id="tilt-timezones"><option value="Asia/Singapore" /><option value="Asia/Kolkata" /><option value="UTC" /><option value="Europe/London" /><option value="America/New_York" /></datalist>
        </FormField>
      </div>
      <p className="text-xs leading-5 text-muted-foreground">Saves the day&apos;s last complete tilt reading at or before this time. The actual reading time is kept. The first complete reading after enabling becomes the fixed onboarding reference.</p>
    </fieldset>
  );
}
