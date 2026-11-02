import { formatMeasurement } from "@/lib/format";
import { useApi } from "@/hooks/useApi";
import { TelemetryApi } from "@/lib/api/resources";
import { StatCard } from "@/components/ui/States";
import { BaselinePanel } from "@/components/devices/BaselinePanel";
import type { DeviceDto, DeviceTypeParameterDto } from "@/types/api";
import type { TiltAxes } from "@/types/tilt";
import { InclinometerOverview } from "@/components/devices/InclinometerOverview";

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground uppercase tracking-wide">{label}</dt>
      <dd className="text-sm text-foreground mt-0.5">{value ?? "—"}</dd>
    </div>
  );
}

function fieldLabel(key: string): string {
  return key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase());
}

export function OverviewTab({ device, parameters = [], tiltAxes }: { device: DeviceDto; parameters?: DeviceTypeParameterDto[]; tiltAxes?: TiltAxes | null }) {
  if (tiltAxes) return <InclinometerOverview device={device} parameters={parameters} axes={tiltAxes} />;
  return <StandardOverview device={device} parameters={parameters} />;
}

function StandardOverview({ device, parameters }: { device: DeviceDto; parameters: DeviceTypeParameterDto[] }) {
  const telemetryQuery = useApi(() => TelemetryApi.latestForDevice(device.id), [device.id]);

  return (
    // items-start: without it, CSS grid stretches both cards to match the
    // taller one by default — "Current telemetry" usually has far fewer
    // fields than "Device information", which left it mostly empty space.
    <div className="grid md:grid-cols-2 gap-4 items-start">
      <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-foreground mb-3">Device information</h2>
        <dl className="grid grid-cols-2 gap-4">
          <Field label="Device Code" value={device.deviceCode} />
          <Field label="Serial Number" value={device.serialNumber} />
          <Field label="Manufacturer" value={device.manufacturer} />
          <Field label="Model" value={device.model} />
          <Field label="Firmware Version" value={device.firmwareVersion} />
          <Field label="Hardware Version" value={device.hardwareVersion} />
          <Field label="Last Seen" value={device.lastSeenAt ? new Date(device.lastSeenAt).toLocaleString() : "Never"} />
        </dl>
      </div>

      <div className="bg-card border border-border rounded-lg p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-foreground mb-3">Current telemetry</h2>
        {telemetryQuery.data?.point ? (
          <>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
              {Object.entries(telemetryQuery.data.point.fields ?? {})
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([key, value]) => (
                  <StatCard key={key} label={parameters.find((p) => p.key === key)?.label ?? fieldLabel(key)} value={formatMeasurement(value, key)} />
                ))}
            </div>
            <p className="text-xs text-muted-foreground mt-3">As of {new Date(telemetryQuery.data.point.timestamp).toLocaleString()}</p>
          </>
        ) : (
          <p className="text-sm text-muted-foreground">No telemetry recorded yet.</p>
        )}
      </div>

      <BaselinePanel device={device} parameters={parameters} />
    </div>
  );
}
