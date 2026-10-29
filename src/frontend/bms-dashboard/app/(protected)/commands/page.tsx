"use client";

import { useState } from "react";
import { Send } from "lucide-react";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { DevicesApi } from "@/lib/api/resources";
import { LoadingState, ErrorState, EmptyState } from "@/components/ui/States";
import { CommandsTab } from "@/components/devices/tabs/CommandsTab";

export default function CommandsPage() {
  const { effectiveTenantId } = useTenantScope();
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);
  const [deviceId, setDeviceId] = useState("");
  const activeId = deviceId || devicesQuery.data?.items[0]?.id || "";

  if (devicesQuery.loading) return <LoadingState />;
  if (devicesQuery.error) return <ErrorState message={devicesQuery.error} />;

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-foreground">Commands</h1>
        <p className="text-sm text-muted-foreground">Send remote commands to a controller and track delivery status.</p>
      </div>

      {!devicesQuery.data || devicesQuery.data.items.length === 0 ? (
        <EmptyState message="No devices to command" hint="Register a device first, then it will be selectable here." icon={Send} />
      ) : (
        <>
          <div>
            <label className="block text-xs font-medium text-muted-foreground mb-1">Controller / device</label>
            <select
              value={activeId}
              onChange={(e) => setDeviceId(e.target.value)}
              className="text-sm border border-input bg-background text-foreground rounded-md px-2 py-1.5 min-w-64"
            >
              {devicesQuery.data.items.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.deviceCode})
                </option>
              ))}
            </select>
          </div>

          {activeId && <CommandsTab deviceId={activeId} />}
        </>
      )}
    </div>
  );
}
