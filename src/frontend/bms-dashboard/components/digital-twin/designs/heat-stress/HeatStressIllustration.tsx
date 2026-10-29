import type { IllustrationProps } from "../../types";
import { HEALTH_STYLE } from "../../limits";

/** WBGT station: black globe, natural wet bulb and shielded dry bulb on a boom, with the logger on the mast. */
export function HeatStressIllustration({ health, running }: IllustrationProps) {
  const alarm = health === "warning" || health === "critical";
  const label = "fill-current text-muted-foreground";
  return (
    <svg viewBox="0 0 520 320" className="w-full h-auto" role="img" aria-label="WBGT heat stress station">
      <ellipse cx="270" cy="304" rx="240" ry="9" fill="#0f172a" opacity="0.08" />

      {/* sun */}
      <g className={running ? "motion-safe:animate-pulse" : undefined}>
        <circle cx="482" cy="40" r="20" fill="#fbbf24" />
        {Array.from({ length: 8 }, (_, i) => {
          const a = (i * Math.PI) / 4;
          return (
            <line key={i} x1={482 + 26 * Math.cos(a)} y1={40 + 26 * Math.sin(a)} x2={482 + 34 * Math.cos(a)} y2={40 + 34 * Math.sin(a)} stroke="#fbbf24" strokeWidth="3" strokeLinecap="round" />
          );
        })}
      </g>

      {/* mast, legs, boom */}
      <rect x="434" y="96" width="12" height="190" fill="#64748b" />
      <line x1="440" y1="282" x2="400" y2="302" stroke="#475569" strokeWidth="5" strokeLinecap="round" />
      <line x1="440" y1="282" x2="480" y2="302" stroke="#475569" strokeWidth="5" strokeLinecap="round" />
      <rect x="90" y="96" width="356" height="10" rx="5" fill="#64748b" />

      {/* black globe */}
      <line x1="140" y1="106" x2="140" y2="132" stroke="#64748b" strokeWidth="3" />
      <circle cx="140" cy="168" r="36" fill="#111827" />
      <circle cx="128" cy="154" r="9" fill="#ffffff" opacity="0.25" />
      <text x="140" y="228" textAnchor="middle" fontSize="13" className={label}>Globe</text>

      {/* natural wet bulb */}
      <line x1="250" y1="106" x2="250" y2="128" stroke="#64748b" strokeWidth="3" />
      <rect x="242" y="128" width="16" height="72" rx="8" fill="#e0f2fe" stroke="#0284c7" strokeWidth="2" />
      <rect x="247" y="150" width="6" height="46" rx="3" fill="#0ea5e9" />
      <circle cx="250" cy="204" r="12" fill="#0ea5e9" />
      <path d="M250 216 q-6 8 0 14 q6 6 0 12" fill="none" stroke="#38bdf8" strokeWidth="3" />
      <rect x="232" y="236" width="36" height="22" rx="4" fill="#bae6fd" stroke="#0284c7" />
      <text x="250" y="276" textAnchor="middle" fontSize="13" className={label}>Wet bulb</text>

      {/* shielded dry bulb */}
      <line x1="360" y1="106" x2="360" y2="126" stroke="#64748b" strokeWidth="3" />
      {[0, 1, 2, 3, 4].map((i) => (
        <rect key={i} x="328" y={126 + i * 16} width="64" height="10" rx="5" fill="#f8fafc" stroke="#94a3b8" />
      ))}
      <rect x="356" y="132" width="8" height="70" rx="4" fill="#fecaca" stroke="#dc2626" />
      <text x="360" y="228" textAnchor="middle" fontSize="13" className={label}>Dry bulb</text>

      {/* logger on the mast */}
      {alarm && <rect x="408" y="170" width="64" height="52" rx="10" fill="none" stroke={HEALTH_STYLE[health].hex} strokeWidth="5" opacity="0.4" />}
      <rect x="414" y="176" width="52" height="40" rx="6" fill="#334155" />
      <rect x="422" y="184" width="36" height="14" rx="2" fill={running ? "#22c55e" : "#64748b"} opacity="0.85" />
    </svg>
  );
}
