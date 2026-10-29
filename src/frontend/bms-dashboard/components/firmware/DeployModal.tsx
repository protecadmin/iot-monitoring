"use client";

import { useMemo, useState } from "react";
import { Cpu } from "lucide-react";
import { Modal } from "@/components/ui/Modal";
import { StatusBadge, LoadingState, ErrorState } from "@/components/ui/States";
import { useApi } from "@/hooks/useApi";
import { DevicesApi, FirmwareApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import type { FirmwareDeploymentDto, FirmwareDto } from "@/types/api";

export function DeployModal({
  release,
  deviceTypeName,
  tenantId,
  busyDeviceIds,
  onClose,
  onDeployed,
}: {
  release: FirmwareDto;
  deviceTypeName: string;
  tenantId: string | undefined;
  /** Devices already mid-update with this release — they can't be picked again yet. */
  busyDeviceIds: Set<string>;
  onClose: () => void;
  onDeployed: (deployments: FirmwareDeploymentDto[]) => void;
}) {
  // Only devices of the release's type — firmware is built for one type.
  const devicesQuery = useApi(
    () => DevicesApi.list({ tenantId, deviceTypeId: release.deviceTypeId, pageSize: 200 }),
    [tenantId, release.deviceTypeId],
  );
  const devices = useMemo(() => devicesQuery.data?.items ?? [], [devicesQuery.data]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [deploying, setDeploying] = useState(false);

  const selectable = devices.filter((d) => !busyDeviceIds.has(d.id));
  const selectedDevices = devices.filter((d) => selected.has(d.id));
  const offlineSelected = selectedDevices.filter((d) => d.status !== "Online").length;

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function deploy() {
    setError(null);
    setDeploying(true);
    try {
      const result = await FirmwareApi.deploy(release.id, [...selected]);
      onDeployed(result);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Deployment failed");
    } finally {
      setDeploying(false);
    }
  }

  return (
    <Modal title={`Deploy v${release.version} — ${deviceTypeName}`} onClose={onClose} size="lg">
      <div className="space-y-4">
        {devicesQuery.loading && <LoadingState />}
        {devicesQuery.error && <ErrorState message={devicesQuery.error} />}

        {!devicesQuery.loading && !devicesQuery.error && devices.length === 0 && (
          <div className="text-center py-8 text-sm text-muted-foreground">
            <Cpu className="h-6 w-6 mx-auto mb-2" />
            No {deviceTypeName} devices in this tenant.
          </div>
        )}

        {devices.length > 0 && (
          <>
            <div className="flex items-center justify-between text-xs">
              <span className="text-muted-foreground">
                {devices.length} {deviceTypeName} device{devices.length === 1 ? "" : "s"}
              </span>
              <div className="flex gap-3">
                <button type="button" onClick={() => setSelected(new Set(selectable.filter((d) => d.status === "Online").map((d) => d.id)))} className="text-primary hover:underline">
                  Select all Online
                </button>
                <button type="button" onClick={() => setSelected(new Set(selectable.map((d) => d.id)))} className="text-primary hover:underline">
                  Select all
                </button>
                <button type="button" onClick={() => setSelected(new Set())} className="text-muted-foreground hover:underline">
                  Clear
                </button>
              </div>
            </div>

            <div className="border border-border rounded-lg divide-y divide-border max-h-80 overflow-y-auto">
              {devices.map((d) => {
                const busy = busyDeviceIds.has(d.id);
                const alreadyOnVersion = d.firmwareVersion === release.version;
                return (
                  <label
                    key={d.id}
                    className={`flex items-center gap-3 px-3 py-2.5 ${busy ? "opacity-50 cursor-not-allowed" : "cursor-pointer hover:bg-muted/40"}`}
                  >
                    <input type="checkbox" disabled={busy} checked={selected.has(d.id)} onChange={() => toggle(d.id)} className="h-4 w-4" />
                    <div className="flex-1 min-w-0">
                      <div className="text-sm text-foreground truncate">{d.name}</div>
                      <div className="text-xs text-muted-foreground font-mono">{d.deviceCode}</div>
                    </div>
                    <div className="text-right text-xs shrink-0 space-y-1">
                      <StatusBadge status={d.status} />
                      <div className={alreadyOnVersion ? "text-amber-600 dark:text-amber-400" : "text-muted-foreground"}>
                        {busy ? "Update in progress" : alreadyOnVersion ? `Already on v${release.version}` : `Current: ${d.firmwareVersion ? `v${d.firmwareVersion}` : "unknown"}`}
                      </div>
                    </div>
                  </label>
                );
              })}
            </div>

            {offlineSelected > 0 && (
              <p className="text-xs text-amber-600 dark:text-amber-400">
                {offlineSelected} selected device{offlineSelected === 1 ? " is" : "s are"} not Online. {offlineSelected === 1 ? "It" : "They"} will show{" "}
                <span className="font-medium">Waiting for device</span> and receive the update automatically when back Online.
              </p>
            )}
          </>
        )}

        {error && <p className="text-xs text-destructive">{error}</p>}
        <button
          onClick={deploy}
          disabled={deploying || selected.size === 0}
          className="w-full text-sm px-3 py-2 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
        >
          {deploying ? "Deploying…" : selected.size === 0 ? "Select devices to deploy to" : `Deploy to ${selected.size} device${selected.size === 1 ? "" : "s"}`}
        </button>
      </div>
    </Modal>
  );
}
