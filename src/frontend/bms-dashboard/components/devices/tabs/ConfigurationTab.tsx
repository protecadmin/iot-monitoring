"use client";

import { useEffect, useState } from "react";
import { BookOpen, CircleHelp } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { ConfigurationApi } from "@/lib/api/resources";
import { useCan } from "@/lib/auth/permissions";
import { LoadingState, ErrorState } from "@/components/ui/States";
import { ConfigurationGuideModal } from "@/components/devices/ConfigurationGuideModal";

type GuideTab = "Overview" | "Reporting interval" | "Heartbeat & status" | "Temperature threshold";

/** Small "?" beside a field label that opens the guide on that field's tab. */
function FieldHelp({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button type="button" onClick={onClick} title={`About ${label}`} aria-label={`About ${label}`} className="text-muted-foreground hover:text-primary">
      <CircleHelp className="h-3.5 w-3.5" />
    </button>
  );
}

export function ConfigurationTab({ deviceId }: { deviceId: string }) {
  const query = useApi(() => ConfigurationApi.get(deviceId), [deviceId]);
  const [reportingInterval, setReportingInterval] = useState(5);
  const [heartbeatInterval, setHeartbeatInterval] = useState(30);
  const [threshold, setThreshold] = useState<string>("");
  const canEdit = useCan()("configuration.edit");
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [guideTab, setGuideTab] = useState<GuideTab | null>(null);

  useEffect(() => {
    if (query.data) {
      setReportingInterval(query.data.reportingIntervalSeconds);
      setHeartbeatInterval(query.data.heartbeatIntervalSeconds);
      setThreshold(query.data.temperatureThreshold?.toString() ?? "");
    }
  }, [query.data]);

  if (query.loading) return <LoadingState />;
  if (query.error) return <ErrorState message={query.error} />;

  async function handleSave() {
    setSaving(true);
    setSaved(false);
    try {
      await ConfigurationApi.update(deviceId, {
        reportingIntervalSeconds: reportingInterval,
        heartbeatIntervalSeconds: heartbeatInterval,
        temperatureThreshold: threshold ? Number(threshold) : null,
      });
      setSaved(true);
      query.refetch();
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="bg-card border border-border rounded-lg p-4 max-w-md space-y-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <p className="text-xs text-muted-foreground">Settings pushed to the device itself — how often it reports and signs of life.</p>
        <button
          type="button"
          onClick={() => setGuideTab("Overview")}
          className="flex items-center gap-1.5 text-xs px-2.5 py-1 rounded-md border border-input text-foreground hover:bg-muted shrink-0"
        >
          <BookOpen className="h-3.5 w-3.5" /> Configuration guide
        </button>
      </div>
      <div>
        <label className="flex items-center gap-1.5 text-sm font-medium text-foreground mb-1">
          Reporting interval (seconds) <FieldHelp label="reporting interval" onClick={() => setGuideTab("Reporting interval")} />
        </label>
        <input
          readOnly={!canEdit}
          type="number"
          min={1}
          value={reportingInterval}
          onChange={(e) => setReportingInterval(Number(e.target.value))}
          className="w-full border border-input rounded-md px-3 py-1.5 text-sm"
        />
      </div>
      <div>
        <label className="flex items-center gap-1.5 text-sm font-medium text-foreground mb-1">
          Heartbeat interval (seconds) <FieldHelp label="heartbeat interval" onClick={() => setGuideTab("Heartbeat & status")} />
        </label>
        <input
          readOnly={!canEdit}
          type="number"
          min={1}
          value={heartbeatInterval}
          onChange={(e) => setHeartbeatInterval(Number(e.target.value))}
          className="w-full border border-input rounded-md px-3 py-1.5 text-sm"
        />
      </div>
      <div>
        <label className="flex items-center gap-1.5 text-sm font-medium text-foreground mb-1">
          Temperature threshold (optional) <FieldHelp label="temperature threshold" onClick={() => setGuideTab("Temperature threshold")} />
        </label>
        <input
          readOnly={!canEdit}
          type="number"
          value={threshold}
          onChange={(e) => setThreshold(e.target.value)}
          className="w-full border border-input rounded-md px-3 py-1.5 text-sm"
        />
      </div>
      {canEdit && (
        <button
          onClick={handleSave}
          disabled={saving}
          className="text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
        >
          {saving ? "Saving..." : "Save & push to device"}
        </button>
      )}
      {saved && <p className="text-xs text-emerald-600">Configuration pushed to device over MQTT.</p>}
      <p className="text-xs text-muted-foreground">Version {query.data?.version} · sent via MQTT to the device&apos;s config topic.</p>
      {guideTab && (
        <ConfigurationGuideModal
          deviceId={deviceId}
          values={{
            reportingIntervalSeconds: reportingInterval,
            heartbeatIntervalSeconds: heartbeatInterval,
            temperatureThreshold: threshold ? Number(threshold) : null,
          }}
          version={query.data?.version}
          initialTab={guideTab}
          onClose={() => setGuideTab(null)}
        />
      )}
    </div>
  );
}
