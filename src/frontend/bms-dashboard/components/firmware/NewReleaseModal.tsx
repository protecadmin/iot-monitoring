"use client";

import { useRef, useState } from "react";
import { FileUp, X } from "lucide-react";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { FirmwareApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import type { DeviceTypeDto, FirmwareDto } from "@/types/api";

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const MAX_MB = 64;

export function NewReleaseModal({
  deviceTypes,
  onClose,
  onCreated,
}: {
  deviceTypes: DeviceTypeDto[];
  onClose: () => void;
  onCreated: (release: FirmwareDto) => void;
}) {
  const [deviceTypeId, setDeviceTypeId] = useState("");
  const [version, setVersion] = useState("");
  const [releaseNotes, setReleaseNotes] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  function pick(f: File | undefined) {
    if (!f) return;
    setError(f.size > MAX_MB * 1024 * 1024 ? `The file is larger than the ${MAX_MB} MB limit` : null);
    setFile(f);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!deviceTypeId) return setError("Choose a device type");
    if (!version.trim()) return setError("Enter a version, e.g. 1.2.0");
    if (!file) return setError("Choose the firmware file to upload");
    if (file.size > MAX_MB * 1024 * 1024) return setError(`The file is larger than the ${MAX_MB} MB limit`);

    setSaving(true);
    try {
      const created = await FirmwareApi.create({ deviceTypeId, version: version.trim(), releaseNotes: releaseNotes.trim() || undefined, file });
      onCreated(created);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Upload failed");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title="New firmware release" onClose={onClose} size="lg">
      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <FormField label="Device type">
            <select value={deviceTypeId} onChange={(e) => setDeviceTypeId(e.target.value)} className={inputClass}>
              <option value="">Select…</option>
              {deviceTypes.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Version">
            <input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="1.2.0" className={`${inputClass} font-mono`} />
          </FormField>
        </div>

        <FormField label="Firmware file">
          <div
            onClick={() => inputRef.current?.click()}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(e) => {
              e.preventDefault();
              setDragging(false);
              pick(e.dataTransfer.files[0]);
            }}
            className={`cursor-pointer rounded-lg border-2 border-dashed px-4 py-6 text-center transition-colors ${
              dragging ? "border-primary bg-primary/5" : "border-border hover:border-primary/50 hover:bg-muted/40"
            }`}
          >
            <input ref={inputRef} type="file" className="hidden" onChange={(e) => pick(e.target.files?.[0])} />
            {file ? (
              <div className="flex items-center justify-center gap-3">
                <FileUp className="h-5 w-5 text-primary shrink-0" />
                <div className="text-left min-w-0">
                  <div className="text-sm font-medium text-foreground truncate">{file.name}</div>
                  <div className="text-xs text-muted-foreground">{formatBytes(file.size)}</div>
                </div>
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    setFile(null);
                    setError(null);
                  }}
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-muted"
                  aria-label="Remove file"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            ) : (
              <div className="space-y-1">
                <FileUp className="h-6 w-6 mx-auto text-muted-foreground" />
                <div className="text-sm text-foreground">Drop the firmware file here, or click to choose</div>
                <div className="text-xs text-muted-foreground">Any binary (.bin, .hex, .img…) up to {MAX_MB} MB</div>
              </div>
            )}
          </div>
        </FormField>

        <FormField label="Release notes (optional)">
          <textarea
            value={releaseNotes}
            onChange={(e) => setReleaseNotes(e.target.value)}
            rows={3}
            placeholder="What changed in this version"
            className={inputClass}
          />
        </FormField>

        <p className="text-xs text-muted-foreground">
          The file is stored on the BMS server and its SHA-256 checksum is calculated there. Devices download it from a signed link and must check the
          checksum before installing.
        </p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-2 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Uploading…" : "Upload release"}
        </button>
      </form>
    </Modal>
  );
}
