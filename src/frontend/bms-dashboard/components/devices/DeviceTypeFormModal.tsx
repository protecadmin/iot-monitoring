"use client";

import { useState } from "react";
import { DeviceTypesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import type { DeviceTypeDto } from "@/types/api";

/** Creates a device type, or edits its name/description when `deviceType` is given. Parameters are managed on the card itself. */
export function DeviceTypeFormModal({
  deviceType,
  onClose,
  onSaved,
}: {
  deviceType?: DeviceTypeDto;
  onClose: () => void;
  onSaved: (saved: DeviceTypeDto) => void;
}) {
  const isEdit = !!deviceType;
  const [name, setName] = useState(deviceType?.name ?? "");
  const [description, setDescription] = useState(deviceType?.description ?? "");
  // Minutes in the form; seconds in the API.
  const [reportMinutes, setReportMinutes] = useState(deviceType?.expectedIntervalSeconds ? String(deviceType.expectedIntervalSeconds / 60) : "");
  const [supportsCommands, setSupportsCommands] = useState(deviceType?.supportsCommands ?? true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const minutes = reportMinutes.trim() === "" ? null : Number(reportMinutes);
    if (minutes != null && !(minutes >= 0.5 && minutes <= 10080)) {
      setError("Reports every: enter minutes between 0.5 and 10080 (7 days), or leave it empty.");
      return;
    }
    setSaving(true);
    try {
      const body = {
        name,
        description: description || undefined,
        expectedIntervalSeconds: minutes == null ? null : Math.round(minutes * 60),
        supportsCommands,
      };
      const saved = isEdit ? await DeviceTypesApi.update(deviceType.id, body) : await DeviceTypesApi.create(body);
      onSaved(saved);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : isEdit ? "Failed to save device type" : "Failed to create device type");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={isEdit ? `Edit ${deviceType.name}` : "New device type"} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <FormField label="Name">
          <input required value={name} onChange={(e) => setName(e.target.value)} placeholder="Smoke Detector" className={inputClass} />
        </FormField>
        <FormField label="Description (optional)">
          <input
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="One line shown under the name, e.g. what it measures"
            className={inputClass}
          />
        </FormField>
        <FormField label="Reports every (minutes, optional)">
          <input
            type="number"
            min={0.5}
            step="any"
            value={reportMinutes}
            onChange={(e) => setReportMinutes(e.target.value)}
            placeholder="e.g. 5 for a sensor that wakes every 5 minutes"
            className={inputClass}
          />
        </FormField>
        <p className="text-xs text-muted-foreground -mt-1">
          For devices that report slower than every minute or so. They then only count as offline, and silence alerts only fire, after missing two
          reports. Leave empty for devices that report continuously.
        </p>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" checked={supportsCommands} onChange={(e) => setSupportsCommands(e.target.checked)} />
          Can receive commands
        </label>
        <p className="text-xs text-muted-foreground -mt-1">Untick for devices that only send data (no remote restart or settings); their Commands tab is hidden.</p>
        {isEdit && (
          <p className="text-xs text-muted-foreground">
            Renaming is safe — devices, firmware and rules refer to the type itself, not its name.
          </p>
        )}
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? (isEdit ? "Saving..." : "Creating...") : isEdit ? "Save changes" : "Create device type"}
        </button>
      </form>
    </Modal>
  );
}
