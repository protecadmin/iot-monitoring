"use client";

import { use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Activity, Bell, Box, FileCode, Info, Send, SlidersHorizontal, type LucideIcon } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi, DevicesApi } from "@/lib/api/resources";
import { useCan, type Permission } from "@/lib/auth/permissions";
import { LoadingState, ErrorState, StatusBadge } from "@/components/ui/States";
import { OverviewTab } from "@/components/devices/tabs/OverviewTab";
import { LiveTab } from "@/components/devices/tabs/LiveTab";
import { AlertsTab } from "@/components/devices/tabs/AlertsTab";
import { CommandsTab } from "@/components/devices/tabs/CommandsTab";
import { ConfigurationTab } from "@/components/devices/tabs/ConfigurationTab";
import { RawMessagesTab } from "@/components/devices/tabs/RawMessagesTab";
import { DigitalTwinView } from "@/components/digital-twin/DigitalTwinView";
import { resolveTwin } from "@/components/digital-twin/registry";
import type { DeviceDto } from "@/types/api";

// Live now carries current-value tiles + chart + raw readings table (all
// off one shared fetch, see useTelemetryHistory) — a separate History tab
// was pure duplication of the same chart with nothing else added.
const TABS: { label: string; icon: LucideIcon; permission?: Permission }[] = [
  { label: "Overview", icon: Info },
  { label: "Digital Twin", icon: Box },
  { label: "Live", icon: Activity },
  { label: "Alerts", icon: Bell },
  { label: "Commands", icon: Send, permission: "technical.view" },
  { label: "Configuration", icon: SlidersHorizontal, permission: "technical.view" },
  { label: "Raw Messages", icon: FileCode, permission: "technical.view" },
];
type Tab = (typeof TABS)[number]["label"];

export default function DeviceDetailsPage({ params }: { params: Promise<{ deviceId: string }> }) {
  const { deviceId } = use(params);
  const [tab, setTab] = useState<Tab>("Overview");
  const can = useCan();
  const tabs = TABS.filter((t) => !t.permission || can(t.permission));

  const query = useApi(() => DevicesApi.get(deviceId), [deviceId]);
  const typesQuery = useApi(() => DeviceTypesApi.list(), []);
  // The device as returned by a save on this page (e.g. new twin settings).
  const [savedDevice, setSavedDevice] = useState<DeviceDto | null>(null);

  if (query.loading) return <LoadingState />;
  if (query.error) return <ErrorState message={query.error} />;
  if (!query.data) return null;
  const device = savedDevice?.id === query.data.id ? savedDevice : query.data;
  const deviceType = typesQuery.data?.find((t) => t.id === device.deviceTypeId);
  const deviceTypeName = deviceType?.name;
  // Only sensor types with twin templates (components/digital-twin/designs) get the tab.
  const twin = resolveTwin(device, deviceTypeName);
  const visibleTabs = tabs.filter(
    (t) => (t.label !== "Digital Twin" || twin) && (t.label !== "Commands" || deviceType?.supportsCommands !== false),
  );
  const interval = deviceType?.expectedIntervalSeconds;

  return (
    <div className="space-y-4">
      <div>
        <Link href="/devices" className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground mb-2">
          <ArrowLeft className="h-3.5 w-3.5" /> Devices
        </Link>
        <div className="flex items-center gap-3">
          <h1 className="text-xl font-semibold text-foreground">{device.name}</h1>
          <StatusBadge status={device.status} />
        </div>
        <p className="text-sm text-muted-foreground">
          {device.deviceCode}
          {interval ? <> · Reports every {interval >= 60 ? `${Math.round((interval / 60) * 10) / 10} min` : `${interval} s`}</> : null}
          {device.lastSeenAt && <> · Last seen {new Date(device.lastSeenAt).toLocaleString()}</>}
        </p>
      </div>

      <div className="border-b border-border flex gap-1 overflow-x-auto">
        {visibleTabs.map(({ label, icon: Icon }) => (
          <button
            key={label}
            onClick={() => setTab(label)}
            className={`flex items-center gap-1.5 px-3 py-2 text-sm whitespace-nowrap border-b-2 -mb-px ${
              tab === label ? "border-primary text-primary font-medium" : "border-transparent text-muted-foreground hover:text-foreground"
            }`}
          >
            <Icon className="h-3.5 w-3.5" />
            {label}
          </button>
        ))}
      </div>

      <div>
        {tab === "Overview" && <OverviewTab device={device} parameters={deviceType?.parameters ?? []} />}
        {tab === "Digital Twin" && twin && <DigitalTwinView device={device} twin={twin} deviceTypeName={deviceTypeName} onDeviceChange={setSavedDevice} />}
        {tab === "Live" && <LiveTab deviceId={device.id} parameters={deviceType?.parameters ?? []} timeSource={device.timeSource} />}
        {tab === "Alerts" && <AlertsTab deviceId={device.id} />}
        {tab === "Commands" && tabs.some((t) => t.label === "Commands") && <CommandsTab deviceId={device.id} />}
        {tab === "Configuration" && tabs.some((t) => t.label === "Configuration") && <ConfigurationTab deviceId={device.id} />}
        {tab === "Raw Messages" && tabs.some((t) => t.label === "Raw Messages") && <RawMessagesTab deviceId={device.id} />}
      </div>
    </div>
  );
}
