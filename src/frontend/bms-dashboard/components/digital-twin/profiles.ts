import type { MetricProfile } from "./types";

/** Vibration velocity, ISO 10816-style zones. Fits any rotating equipment. */
export const vibrationProfile: MetricProfile = {
  id: "vibration",
  deviceTypes: ["Vibration Sensor"],
  primary: { key: "vibration", label: "Vibration (velocity)", unit: "mm/s", decimals: 2, defaultLimits: { warning: 4.5, critical: 7.1 } },
  secondary: [
    { key: "vibrationX", label: "Axis X", unit: "mm/s", decimals: 2 },
    { key: "vibrationY", label: "Axis Y", unit: "mm/s", decimals: 2 },
    { key: "vibrationZ", label: "Axis Z", unit: "mm/s", decimals: 2 },
    { key: "temperature", label: "Temperature", unit: "°C", decimals: 1 },
    { key: "humidity", label: "Humidity", unit: "%", decimals: 1 },
  ],
};

/** Workplace heat stress (WBGT). */
export const heatStressProfile: MetricProfile = {
  id: "heat-stress",
  deviceTypes: ["WBGT Sensor"],
  primary: { key: "wbgt", label: "WBGT", unit: "°C", decimals: 1, defaultLimits: { warning: 30, critical: 32 } },
  secondary: [
    { key: "dryBulb", label: "Dry bulb", unit: "°C", decimals: 1 },
    { key: "wetBulb", label: "Wet bulb", unit: "°C", decimals: 1 },
    { key: "globeTemp", label: "Globe", unit: "°C", decimals: 1 },
    { key: "humidity", label: "Humidity", unit: "%", decimals: 1 },
  ],
};

/** Occupational noise; 85 dB(A) is the usual hearing-protection action level. */
export const noiseProfile: MetricProfile = {
  id: "noise",
  deviceTypes: ["Noise Sensor"],
  primary: { key: "noiseLevel", label: "Noise level", unit: "dB(A)", decimals: 1, defaultLimits: { warning: 85, critical: 90 } },
  secondary: [{ key: "noisePeak", label: "Peak", unit: "dB(C)", decimals: 1 }],
};

export const PROFILES: MetricProfile[] = [vibrationProfile, heatStressProfile, noiseProfile];
