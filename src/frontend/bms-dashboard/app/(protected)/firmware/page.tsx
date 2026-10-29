"use client";

import { useMemo, useState } from "react";
import { BookOpen, Download, HardDriveDownload, PackagePlus, RotateCw, Rocket, Trash2, TriangleAlert } from "lucide-react";
import { useCan } from "@/lib/auth/permissions";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { DeviceTypesApi, DevicesApi, FirmwareApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui/States";
import { ConfirmModal } from "@/components/ui/ConfirmModal";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { NewReleaseModal, formatBytes } from "@/components/firmware/NewReleaseModal";
import { DeployModal } from "@/components/firmware/DeployModal";
import { FirmwareGuideModal } from "@/components/firmware/FirmwareGuideModal";
import type { FirmwareDeploymentDto, FirmwareDto } from "@/types/api";

const STAGES = ["Pending", "Downloading", "Verifying", "Installing", "Rebooting", "Success"];
const FINISHED = new Set(["Success", "Failed", "Timeout"]);

/** Six-segment bar showing how far an update got — red from the point it stopped when it failed. */
function StageBar({ status }: { status: string }) {
  const failed = status === "Failed" || status === "Timeout";
  const reached = status === "WaitingForDevice" ? -1 : failed ? -1 : STAGES.indexOf(status);
  return (
    <div className="flex gap-0.5 mt-1" title={status}>
      {STAGES.map((stage, i) => (
        <span
          key={stage}
          className={`h-1 w-4 rounded-full ${
            failed ? "bg-destructive/60" : i <= reached ? (status === "Success" ? "bg-emerald-500" : "bg-primary") : "bg-muted"
          }`}
        />
      ))}
    </div>
  );
}

function formatDate(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

export default function FirmwarePage() {
  const canManage = useCan()("firmware.manage");
  const { effectiveTenantId } = useTenantScope();

  const firmwareQuery = useApi(() => FirmwareApi.list(), []);
  const deviceTypesQuery = useApi(() => DeviceTypesApi.list(), []);
  const devicesQuery = useApi(() => DevicesApi.list({ tenantId: effectiveTenantId, pageSize: 200 }), [effectiveTenantId]);

  const [typeFilter, setTypeFilter] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [deploying, setDeploying] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [guideOpen, setGuideOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState<string | null>(null);

  const releases = useMemo(
    () => (firmwareQuery.data ?? []).filter((f) => !typeFilter || f.deviceTypeId === typeFilter),
    [firmwareQuery.data, typeFilter],
  );
  const selected: FirmwareDto | undefined = releases.find((f) => f.id === selectedId) ?? releases[0];

  const deploymentsQuery = useApi(() => (selected ? FirmwareApi.deployments(selected.id) : Promise.resolve([])), [selected?.id, effectiveTenantId]);
  const [live, setLive] = useState<{ firmwareId: string; items: FirmwareDeploymentDto[] } | null>(null);
  const deployments = useMemo(
    () => (live && live.firmwareId === selected?.id ? live.items : deploymentsQuery.data) ?? [],
    [live, selected?.id, deploymentsQuery.data],
  );

  useRealtime({
    FirmwareDeploymentUpdated: (payload) => {
      const update = payload as Partial<FirmwareDeploymentDto> & { id: string; firmwareId: string };
      if (!selected || update.firmwareId !== selected.id) return;
      setLive((prev) => {
        const base = prev && prev.firmwareId === selected.id ? prev.items : (deploymentsQuery.data ?? []);
        const exists = base.some((d) => d.id === update.id);
        const items = exists
          ? base.map((d) => (d.id === update.id ? { ...d, ...update } : d))
          : [update as FirmwareDeploymentDto, ...base];
        return { firmwareId: selected.id, items };
      });
    },
  });

  const typeName = useMemo(() => {
    const map = new Map<string, string>();
    for (const t of deviceTypesQuery.data ?? []) map.set(t.id, t.name);
    return (id: string) => map.get(id) ?? "Unknown type";
  }, [deviceTypesQuery.data]);

  const deviceById = useMemo(() => {
    const map = new Map<string, { name: string; code: string }>();
    for (const d of devicesQuery.data?.items ?? []) map.set(d.id, { name: d.name, code: d.deviceCode });
    return map;
  }, [devicesQuery.data]);

  const summary = useMemo(() => {
    const count = (pred: (s: string) => boolean) => deployments.filter((d) => pred(d.status)).length;
    return {
      total: deployments.length,
      success: count((s) => s === "Success"),
      failed: count((s) => s === "Failed" || s === "Timeout"),
      waiting: count((s) => s === "WaitingForDevice"),
      inProgress: count((s) => !FINISHED.has(s) && s !== "WaitingForDevice"),
    };
  }, [deployments]);

  const busyDeviceIds = useMemo(
    () => new Set(deployments.filter((d) => !FINISHED.has(d.status)).map((d) => d.deviceId)),
    [deployments],
  );

  function refreshDeployments() {
    setLive(null);
    deploymentsQuery.refetch();
  }

  async function retry(d: FirmwareDeploymentDto) {
    setActionError(null);
    setRetrying(d.id);
    try {
      await FirmwareApi.retry(d.id);
      refreshDeployments();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Retry failed");
    } finally {
      setRetrying(null);
    }
  }

  async function download(release: FirmwareDto) {
    setActionError(null);
    try {
      await FirmwareApi.downloadFile(release);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Download failed");
    }
  }

  if (firmwareQuery.loading || deviceTypesQuery.loading) return <LoadingState />;
  if (firmwareQuery.error) return <ErrorState message={firmwareQuery.error} />;

  const allReleases = firmwareQuery.data ?? [];

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-2">
          <HardDriveDownload className="h-5 w-5 text-muted-foreground mt-0.5" />
          <div>
            <h1 className="text-xl font-semibold text-foreground">Firmware / OTA</h1>
            <p className="text-sm text-muted-foreground">Upload firmware per device type and roll it out to devices over the air.</p>
          </div>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setGuideOpen(true)}
            className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md border border-input text-foreground hover:bg-muted"
          >
            <BookOpen className="h-4 w-4" /> Firmware guide
          </button>
          {canManage && (
            <button
              onClick={() => setCreating(true)}
              className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
            >
              <PackagePlus className="h-4 w-4" /> New release
            </button>
          )}
        </div>
      </div>

      {allReleases.length === 0 ? (
        <EmptyState
          message="No firmware releases yet"
          hint={canManage ? "Upload a firmware file for a device type to start rolling it out." : "An Admin can upload firmware releases here."}
          icon={HardDriveDownload}
        />
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-5 gap-4 items-start">
          {/* Releases */}
          <div className="lg:col-span-2 bg-card border border-border rounded-lg shadow-sm overflow-hidden">
            <div className="flex items-center justify-between gap-2 px-4 py-3 border-b border-border">
              <h2 className="text-sm font-semibold text-foreground">Releases</h2>
              <select
                value={typeFilter}
                onChange={(e) => setTypeFilter(e.target.value)}
                className="text-xs border border-input bg-background text-foreground rounded-md px-2 py-1"
              >
                <option value="">All device types</option>
                {deviceTypesQuery.data
                  ?.filter((t) => allReleases.some((f) => f.deviceTypeId === t.id))
                  .map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
              </select>
            </div>
            <div className="divide-y divide-border max-h-[32rem] overflow-y-auto">
              {releases.map((f) => (
                <button
                  key={f.id}
                  onClick={() => {
                    setSelectedId(f.id);
                    setActionError(null);
                  }}
                  className={`w-full text-left px-4 py-3 transition-colors ${
                    selected?.id === f.id ? "bg-primary/5 border-l-2 border-primary" : "border-l-2 border-transparent hover:bg-muted/40"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-sm font-semibold text-foreground">v{f.version}</span>
                    {!f.hasFile && (
                      <span className="inline-flex items-center gap-1 text-[10px] px-1.5 py-0.5 rounded bg-warning-muted text-warning-muted-foreground">
                        <TriangleAlert className="h-3 w-3" /> No file
                      </span>
                    )}
                  </div>
                  <div className="text-xs text-foreground mt-0.5">{typeName(f.deviceTypeId)}</div>
                  <div className="text-xs text-muted-foreground mt-0.5 truncate">
                    {f.hasFile ? `${f.fileName} · ${formatBytes(f.sizeBytes)}` : "Created before file upload — can't be deployed"} ·{" "}
                    {new Date(f.releaseDate).toLocaleDateString()}
                  </div>
                </button>
              ))}
              {releases.length === 0 && <p className="px-4 py-6 text-xs text-muted-foreground text-center">No releases for this device type.</p>}
            </div>
          </div>

          {/* Selected release */}
          {selected && (
            <div className="lg:col-span-3 space-y-4">
              <div className="bg-card border border-border rounded-lg shadow-sm p-4 space-y-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="font-mono text-lg font-semibold text-foreground">v{selected.version}</h2>
                      <span className="text-xs px-2 py-0.5 rounded-full bg-muted text-muted-foreground">{typeName(selected.deviceTypeId)}</span>
                    </div>
                    <p className="text-xs text-muted-foreground mt-0.5">Uploaded {formatDate(selected.releaseDate)}</p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {canManage && (
                      <button
                        onClick={() => setDeploying(true)}
                        disabled={!selected.hasFile}
                        title={selected.hasFile ? "Send this firmware to devices" : "This release has no file — upload a new release"}
                        className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                      >
                        <Rocket className="h-4 w-4" /> Deploy
                      </button>
                    )}
                    {selected.hasFile && (
                      <button
                        onClick={() => download(selected)}
                        className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md border border-input text-foreground hover:bg-muted"
                      >
                        <Download className="h-4 w-4" /> Download
                      </button>
                    )}
                    {canManage && (
                      <button
                        onClick={() => setDeleting(true)}
                        className="flex items-center gap-1.5 text-sm px-3 py-1.5 rounded-md border border-input text-destructive hover:bg-destructive/5"
                      >
                        <Trash2 className="h-4 w-4" /> Delete
                      </button>
                    )}
                  </div>
                </div>

                {selected.hasFile ? (
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 text-xs">
                    <div>
                      <div className="text-muted-foreground">File</div>
                      <div className="text-foreground mt-0.5 truncate" title={selected.fileName}>
                        {selected.fileName}
                      </div>
                    </div>
                    <div>
                      <div className="text-muted-foreground">Size</div>
                      <div className="text-foreground mt-0.5">{formatBytes(selected.sizeBytes)}</div>
                    </div>
                    <div className="sm:col-span-3">
                      <CodeBlock label="SHA-256 — devices verify the download against this" value={selected.sha256Checksum} />
                    </div>
                  </div>
                ) : (
                  <p className="text-xs text-warning-muted-foreground bg-warning-muted rounded-md px-3 py-2">
                    This release was created before firmware files were stored, so there is nothing for a device to download. Upload a new release with the file
                    and delete this one.
                  </p>
                )}

                {selected.releaseNotes && (
                  <div className="text-xs">
                    <div className="text-muted-foreground mb-0.5">Release notes</div>
                    <p className="text-foreground whitespace-pre-line">{selected.releaseNotes}</p>
                  </div>
                )}
              </div>

              {/* Rollout */}
              <div className="bg-card border border-border rounded-lg shadow-sm overflow-hidden">
                <div className="px-4 py-3 border-b border-border space-y-3">
                  <div className="flex items-center justify-between">
                    <h3 className="text-sm font-semibold text-foreground">Rollout</h3>
                    <span className="text-xs text-muted-foreground">Updates live</span>
                  </div>
                  {summary.total > 0 && (
                    <>
                      <div className="flex h-2 rounded-full overflow-hidden bg-muted">
                        <div className="bg-emerald-500" style={{ width: `${(summary.success / summary.total) * 100}%` }} />
                        <div className="bg-primary" style={{ width: `${(summary.inProgress / summary.total) * 100}%` }} />
                        <div className="bg-amber-500" style={{ width: `${(summary.waiting / summary.total) * 100}%` }} />
                        <div className="bg-destructive" style={{ width: `${(summary.failed / summary.total) * 100}%` }} />
                      </div>
                      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
                        <span className="text-foreground font-medium">{summary.total} device{summary.total === 1 ? "" : "s"}</span>
                        <span className="text-emerald-700 dark:text-emerald-400">● {summary.success} succeeded</span>
                        <span className="text-primary">● {summary.inProgress} in progress</span>
                        <span className="text-amber-600 dark:text-amber-400">● {summary.waiting} waiting</span>
                        <span className="text-destructive">● {summary.failed} failed</span>
                      </div>
                    </>
                  )}
                </div>

                {actionError && <p className="px-4 pt-3 text-xs text-destructive">{actionError}</p>}

                {deploymentsQuery.loading && <LoadingState />}
                {!deploymentsQuery.loading && deployments.length === 0 && (
                  <p className="px-4 py-8 text-center text-xs text-muted-foreground">
                    Not deployed yet.{canManage && selected.hasFile ? " Click Deploy to send it to devices." : ""}
                  </p>
                )}
                {deployments.length > 0 && (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50 text-muted-foreground text-xs uppercase tracking-wide">
                        <tr>
                          <th className="text-left px-4 py-2 font-medium">Device</th>
                          <th className="text-left px-4 py-2 font-medium">Status</th>
                          <th className="text-left px-4 py-2 font-medium">Sent</th>
                          <th className="text-left px-4 py-2 font-medium">Finished</th>
                          <th className="px-4 py-2" />
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border">
                        {deployments.map((d) => {
                          const device = deviceById.get(d.deviceId);
                          const retryable = d.status === "Failed" || d.status === "Timeout";
                          return (
                            <tr key={d.id} className="align-top">
                              <td className="px-4 py-2.5">
                                <div className="text-foreground">{device?.name ?? d.deviceId.slice(0, 8)}</div>
                                {device && <div className="text-xs text-muted-foreground font-mono">{device.code}</div>}
                              </td>
                              <td className="px-4 py-2.5">
                                <StatusBadge status={d.status} />
                                <StageBar status={d.status} />
                                {d.error && <div className="text-xs text-destructive mt-1 max-w-64">{d.error}</div>}
                                {d.attemptCount > 1 && <div className="text-[11px] text-muted-foreground mt-0.5">Attempt {d.attemptCount}</div>}
                              </td>
                              <td className="px-4 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{formatDate(d.startedAt)}</td>
                              <td className="px-4 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{formatDate(d.completedAt)}</td>
                              <td className="px-4 py-2.5 text-right">
                                {canManage && retryable && (
                                  <button
                                    onClick={() => retry(d)}
                                    disabled={retrying === d.id}
                                    className="inline-flex items-center gap-1 text-xs text-primary hover:underline disabled:opacity-50"
                                  >
                                    <RotateCw className="h-3 w-3" /> {retrying === d.id ? "Retrying…" : "Retry"}
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {creating && deviceTypesQuery.data && (
        <NewReleaseModal
          deviceTypes={deviceTypesQuery.data}
          onClose={() => setCreating(false)}
          onCreated={(release) => {
            setTypeFilter("");
            setSelectedId(release.id);
            firmwareQuery.refetch();
          }}
        />
      )}

      {deploying && selected && (
        <DeployModal
          release={selected}
          deviceTypeName={typeName(selected.deviceTypeId)}
          tenantId={effectiveTenantId}
          busyDeviceIds={busyDeviceIds}
          onClose={() => setDeploying(false)}
          onDeployed={() => {
            refreshDeployments();
            devicesQuery.refetch();
          }}
        />
      )}

      {deleting && selected && (
        <ConfirmModal
          title="Delete firmware release"
          message={`Delete v${selected.version} for ${typeName(selected.deviceTypeId)}? Its file and deployment history are removed. Devices already running it are not affected. Not allowed while an update is still in progress.`}
          onClose={() => setDeleting(false)}
          onConfirm={async () => {
            await FirmwareApi.remove(selected.id);
            setSelectedId(null);
            setLive(null);
            firmwareQuery.refetch();
          }}
        />
      )}

      {guideOpen && <FirmwareGuideModal release={selected} onClose={() => setGuideOpen(false)} />}
    </div>
  );
}
