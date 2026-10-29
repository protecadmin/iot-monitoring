"use client";

import { useState } from "react";
import { Check } from "lucide-react";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import { DevicesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import type { DeviceDto } from "@/types/api";
import type { ResolvedTwin } from "./types";
import { templatesFor } from "./registry";

/**
 * Chooses how a device's twin is drawn: which equipment picture, where on it
 * the sensor is fitted, and what the equipment is called. Saved on the device,
 * so every user sees the same twin.
 */
export function TwinSettingsModal({
  device,
  twin,
  onClose,
  onSaved,
}: {
  device: DeviceDto;
  twin: ResolvedTwin;
  onClose: () => void;
  onSaved: (device: DeviceDto) => void;
}) {
  const options = templatesFor(twin.profile);
  const [templateId, setTemplateId] = useState(twin.template.id);
  const template = options.find((t) => t.id === templateId) ?? options[0];
  const [mountId, setMountId] = useState(twin.mountPoint.id);
  const mount = template.mountPoints.find((m) => m.id === mountId) ?? template.mountPoints[0];
  const [assetName, setAssetName] = useState(device.digitalTwin?.assetName ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function pickTemplate(id: string) {
    setTemplateId(id);
    const next = options.find((t) => t.id === id);
    // Keep the same sensor position when the new picture has it (e.g. "Motor – drive end").
    if (next && !next.mountPoints.some((m) => m.id === mountId)) setMountId(next.mountPoints[0].id);
  }

  async function save(reset = false) {
    setSaving(true);
    setError(null);
    try {
      const updated = await DevicesApi.setDigitalTwin(
        device.id,
        reset ? { template: null, mountPoint: null, assetName: null } : { template: template.id, mountPoint: mount.id, assetName: assetName.trim() || null },
      );
      onSaved(updated);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't save the twin settings");
    } finally {
      setSaving(false);
    }
  }

  const { Illustration } = template;

  return (
    <Modal title={`Digital twin – ${device.name}`} onClose={onClose} size="xl">
      <div className="space-y-5">
        <section className="space-y-2">
          <h3 className="text-sm font-medium text-foreground">1. Equipment</h3>
          <div className="grid gap-3 sm:grid-cols-3">
            {options.map((t) => {
              const selected = t.id === template.id;
              const Picture = t.Illustration;
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => pickTemplate(t.id)}
                  aria-pressed={selected}
                  className={`relative text-left rounded-lg border p-2 transition-colors ${
                    selected ? "border-primary ring-2 ring-primary/30 bg-primary/5" : "border-border hover:border-primary/50"
                  }`}
                >
                  {selected && (
                    <span className="absolute right-2 top-2 h-5 w-5 rounded-full bg-primary text-primary-foreground flex items-center justify-center">
                      <Check className="h-3.5 w-3.5" />
                    </span>
                  )}
                  <div className="rounded-md bg-muted/40 px-1">
                    <Picture health="normal" running={false} />
                  </div>
                  <div className="mt-1.5 text-sm font-medium text-foreground">{t.name}</div>
                  <div className="text-xs text-muted-foreground">{t.description}</div>
                </button>
              );
            })}
          </div>
          {options.length === 1 && <p className="text-xs text-muted-foreground">This sensor type has one template so far.</p>}
        </section>

        <section className="space-y-2">
          <h3 className="text-sm font-medium text-foreground">2. Sensor position</h3>
          <p className="text-xs text-muted-foreground">Click where the sensor is fitted on the equipment.</p>
          <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_220px] items-start">
            <div className="relative rounded-lg border border-border bg-gradient-to-b from-muted/40 to-card p-3">
              <div className="relative">
                <Illustration health="normal" running={false} />
                {template.mountPoints.map((m) => {
                  const active = m.id === mount.id;
                  return (
                    <button
                      key={m.id}
                      type="button"
                      onClick={() => setMountId(m.id)}
                      title={m.label}
                      aria-label={m.label}
                      aria-pressed={active}
                      className={`absolute -translate-x-1/2 -translate-y-1/2 h-6 w-6 rounded-full text-[11px] font-bold flex items-center justify-center ring-2 ring-white transition-transform ${
                        active ? "bg-emerald-500 text-white scale-110" : "bg-white text-slate-700 border border-slate-400 hover:scale-110"
                      }`}
                      style={{ left: `${(m.x / template.viewBox.width) * 100}%`, top: `${(m.y / template.viewBox.height) * 100}%` }}
                    >
                      {active ? "S" : "+"}
                    </button>
                  );
                })}
              </div>
            </div>
            <div className="space-y-1.5">
              {template.mountPoints.map((m) => (
                <label key={m.id} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input type="radio" name="mount" checked={m.id === mount.id} onChange={() => setMountId(m.id)} className="h-4 w-4" />
                  <span className={m.id === mount.id ? "text-foreground font-medium" : "text-muted-foreground"}>{m.label}</span>
                </label>
              ))}
            </div>
          </div>
        </section>

        <section className="space-y-2">
          <h3 className="text-sm font-medium text-foreground">3. Name</h3>
          <FormField label="Equipment name (optional)">
            <input value={assetName} onChange={(e) => setAssetName(e.target.value)} maxLength={80} placeholder={template.assetLabel} className={inputClass} />
          </FormField>
          <p className="text-xs text-muted-foreground">Shown on the twin, e.g. &quot;Cooling Water Pump P-2&quot;. Leave empty to use &quot;{template.assetLabel}&quot;.</p>
        </section>

        {error && <p className="text-xs text-destructive">{error}</p>}
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={() => save()}
            disabled={saving}
            className="text-sm px-4 py-2 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          >
            {saving ? "Saving…" : "Save twin"}
          </button>
          {!twin.isDefault && (
            <button onClick={() => save(true)} disabled={saving} className="text-sm px-3 py-2 rounded-md border border-input hover:bg-muted disabled:opacity-50">
              Reset to default
            </button>
          )}
          <span className="text-xs text-muted-foreground sm:ml-auto">Everyone viewing this device sees the same twin.</span>
        </div>
      </div>
    </Modal>
  );
}
