import type { DeviceDto } from "@/types/api";
import type { MetricProfile, ResolvedTwin, TwinTemplate } from "./types";
import { PROFILES } from "./profiles";
import { compressorTemplate } from "./designs/compressor";
import { pumpTemplate } from "./designs/pump";
import { motorFanTemplate } from "./designs/motor-fan";
import { heatStressTemplate } from "./designs/heat-stress";
import { noiseTemplate } from "./designs/noise";

/**
 * Every twin template. To add equipment: create designs/<id>/ with an SVG
 * Illustration and an index.ts exporting a TwinTemplate (its profile says
 * which sensors can use it), then list it here. The first template of a
 * profile is the default for devices nobody has configured yet.
 */
export const TEMPLATES: TwinTemplate[] = [compressorTemplate, pumpTemplate, motorFanTemplate, heatStressTemplate, noiseTemplate];

export function profileForDeviceType(deviceTypeName: string | undefined): MetricProfile | undefined {
  if (!deviceTypeName) return undefined;
  const name = deviceTypeName.trim().toLowerCase();
  return PROFILES.find((p) => p.deviceTypes.some((t) => t.toLowerCase() === name));
}

export function templatesFor(profile: MetricProfile): TwinTemplate[] {
  return TEMPLATES.filter((t) => t.profile === profile.id);
}

/**
 * The twin a device shows: its saved template, sensor position and name, or
 * the defaults when nothing is saved (or a saved id no longer exists).
 * Undefined when the device type has no twin at all.
 */
export function resolveTwin(device: DeviceDto, deviceTypeName: string | undefined): ResolvedTwin | undefined {
  const profile = profileForDeviceType(deviceTypeName);
  if (!profile) return undefined;
  const options = templatesFor(profile);
  if (options.length === 0) return undefined;

  const saved = device.digitalTwin;
  const template = options.find((t) => t.id === saved?.template) ?? options[0];
  const mountPoint = template.mountPoints.find((m) => m.id === saved?.mountPoint) ?? template.mountPoints[0];
  return {
    profile,
    template,
    mountPoint,
    assetName: saved?.assetName || template.assetLabel,
    isDefault: !saved?.template,
  };
}
