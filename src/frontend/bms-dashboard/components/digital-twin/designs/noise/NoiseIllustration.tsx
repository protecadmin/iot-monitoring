import type { IllustrationProps } from "../../types";
import { HEALTH_STYLE } from "../../limits";

/** Sound level meter on a tripod, facing the machine it monitors. Sound waves take the current health colour. */
export function NoiseIllustration({ health, running }: IllustrationProps) {
  const wave = health === "warning" || health === "critical" ? HEALTH_STYLE[health].hex : "#94a3b8";
  return (
    <svg viewBox="0 0 520 320" className="w-full h-auto" role="img" aria-label="Sound level meter beside a machine">
      <ellipse cx="260" cy="304" rx="240" ry="9" fill="#0f172a" opacity="0.08" />

      {/* machine */}
      <rect x="60" y="104" width="120" height="44" rx="6" fill="#475569" />
      <rect x="40" y="146" width="160" height="136" rx="8" fill="#64748b" />
      <rect x="58" y="166" width="54" height="38" rx="4" fill="#334155" />
      <circle cx="72" cy="185" r="5" fill="#22c55e" />
      <circle cx="90" cy="185" r="5" fill="#f59e0b" />
      <rect x="128" y="166" width="54" height="96" rx="4" fill="#94a3b8" />
      {[0, 1, 2, 3].map((i) => (
        <line key={i} x1="134" y1={180 + i * 20} x2="176" y2={180 + i * 20} stroke="#64748b" strokeWidth="3" />
      ))}
      <rect x="30" y="280" width="180" height="14" rx="7" fill="#334155" />

      {/* sound waves */}
      <g className={running ? "motion-safe:animate-pulse" : undefined} fill="none" stroke={wave} strokeWidth="4" strokeLinecap="round">
        <path d="M224 180 A40 40 0 0 1 224 240" opacity="0.9" />
        <path d="M250 160 A64 64 0 0 1 250 260" opacity="0.6" />
        <path d="M276 140 A88 88 0 0 1 276 280" opacity="0.35" />
      </g>

      {/* sound level meter */}
      <line x1="390" y1="250" x2="350" y2="302" stroke="#475569" strokeWidth="5" strokeLinecap="round" />
      <line x1="390" y1="250" x2="430" y2="302" stroke="#475569" strokeWidth="5" strokeLinecap="round" />
      <line x1="390" y1="250" x2="390" y2="302" stroke="#475569" strokeWidth="5" strokeLinecap="round" />
      <rect x="382" y="100" width="16" height="30" fill="#374151" />
      <circle cx="390" cy="88" r="20" fill="#9ca3af" />
      <circle cx="390" cy="88" r="20" fill="none" stroke="#6b7280" strokeDasharray="3 3" />
      <rect x="360" y="128" width="60" height="124" rx="14" fill="#1f2937" />
      <rect x="368" y="146" width="44" height="28" rx="3" fill={running ? "#22c55e" : "#4b5563"} opacity="0.9" />
      <circle cx="378" cy="196" r="5" fill="#4b5563" />
      <circle cx="402" cy="196" r="5" fill="#4b5563" />
      <rect x="372" y="214" width="36" height="8" rx="4" fill="#4b5563" />
    </svg>
  );
}
