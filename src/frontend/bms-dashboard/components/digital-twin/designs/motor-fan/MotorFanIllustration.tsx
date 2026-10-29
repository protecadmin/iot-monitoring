import type { IllustrationProps } from "../../types";
import { HEALTH_STYLE } from "../../limits";

/** Centrifugal fan (blower): electric motor, shaft on a bearing pedestal, and a scroll housing with its outlet duct. */
export function MotorFanIllustration({ health, running }: IllustrationProps) {
  const alarm = health === "warning" || health === "critical";
  return (
    <svg viewBox="0 0 520 320" className="w-full h-auto" role="img" aria-label="Motor driven centrifugal fan">
      <defs>
        <linearGradient id="fan-motor" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#3b82f6" />
          <stop offset="1" stopColor="#1e40af" />
        </linearGradient>
        <linearGradient id="fan-metal" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#f1f5f9" />
          <stop offset="1" stopColor="#a5b4c4" />
        </linearGradient>
      </defs>
      <ellipse cx="260" cy="302" rx="250" ry="10" fill="#0f172a" opacity="0.08" />

      {/* outlet duct */}
      <rect x="392" y="30" width="56" height="74" fill="url(#fan-metal)" stroke="#64748b" strokeWidth="2" />
      <rect x="384" y="22" width="72" height="12" rx="3" fill="#64748b" />

      {/* scroll housing + wheel */}
      <path d="M362 90 A86 86 0 1 0 448 176 L448 100 L392 100 L392 90 Z" fill="url(#fan-metal)" stroke="#64748b" strokeWidth="2" />
      <circle cx="362" cy="176" r="58" fill="#e2e8f0" stroke="#94a3b8" />
      <g className={running ? "motion-safe:animate-[spin_1.2s_linear_infinite]" : undefined} style={{ transformBox: "fill-box", transformOrigin: "center" }}>
        <circle cx="362" cy="176" r="52" fill="none" stroke="#94a3b8" strokeWidth="2" />
        {Array.from({ length: 12 }, (_, i) => {
          const a = (i * Math.PI) / 6;
          return <line key={i} x1={362 + 20 * Math.cos(a)} y1={176 + 20 * Math.sin(a)} x2={362 + 50 * Math.cos(a + 0.35)} y2={176 + 50 * Math.sin(a + 0.35)} stroke="#475569" strokeWidth="3" strokeLinecap="round" />;
        })}
        <circle cx="362" cy="176" r="10" fill="#475569" />
      </g>

      {/* shaft + bearing pedestal */}
      <rect x="206" y="169" width="90" height="14" fill="#94a3b8" stroke="#64748b" />
      {alarm && <rect x="218" y="150" width="46" height="118" rx="8" fill="none" stroke={HEALTH_STYLE[health].hex} strokeWidth="6" opacity="0.4" />}
      <rect x="224" y="176" width="34" height="88" rx="5" fill="#64748b" />
      <rect x="220" y="158" width="42" height="34" rx="6" fill="url(#fan-metal)" stroke="#475569" />

      {/* motor */}
      <rect x="36" y="140" width="170" height="104" rx="12" fill="url(#fan-motor)" stroke="#1e3a8a" strokeWidth="2" />
      {[0, 1, 2, 3, 4].map((i) => (
        <line key={i} x1={62 + i * 30} y1="150" x2={62 + i * 30} y2="234" stroke="#93c5fd" strokeWidth="3" strokeLinecap="round" opacity="0.5" />
      ))}
      <rect x="22" y="156" width="16" height="72" rx="4" fill="#1e3a8a" />

      {/* skid */}
      <rect x="60" y="244" width="22" height="20" fill="#475569" />
      <rect x="160" y="244" width="22" height="20" fill="#475569" />
      <rect x="20" y="262" width="480" height="20" rx="6" fill="#334155" />
    </svg>
  );
}
