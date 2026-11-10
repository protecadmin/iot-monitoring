"use client";

type WatermarkVariant = "default" | "subtle" | "strong";

export function GlobalWatermark({ variant = "default" }: { variant?: WatermarkVariant }) {
  return <div aria-hidden="true" className={`protec-watermark protec-watermark-${variant}`} />;
}
