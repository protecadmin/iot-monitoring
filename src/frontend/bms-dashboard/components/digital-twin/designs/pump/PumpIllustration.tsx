import type { IllustrationProps } from "../../types";
import { HEALTH_STYLE } from "../../limits";

/** End-suction centrifugal pump: motor, coupling guard, bearing housing and volute casing on a baseplate. */
export function PumpIllustration({ health, running }: IllustrationProps) {
  const alarm = health === "warning" || health === "critical";
  return (
    <svg viewBox="0 0 520 320" className="w-full h-auto" role="img" aria-label="Centrifugal pump">
      <defs>
        <linearGradient id="pmp-motor" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#3b82f6" />
          <stop offset="1" stopColor="#1e40af" />
        </linearGradient>
        <linearGradient id="pmp-metal" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#e2e8f0" />
          <stop offset="1" stopColor="#94a3b8" />
        </linearGradient>
      </defs>
      <ellipse cx="260" cy="302" rx="250" ry="10" fill="#0f172a" opacity="0.08" />

      {/* discharge pipe and flange */}
      <rect x="352" y="56" width="40" height="70" fill="url(#pmp-metal)" stroke="#64748b" />
      <rect x="342" y="48" width="60" height="12" rx="3" fill="#64748b" />
      {/* suction pipe */}
      <rect x="428" y="166" width="78" height="40" fill="url(#pmp-metal)" stroke="#64748b" />
      <rect x="500" y="158" width="12" height="56" rx="3" fill="#64748b" />

      {/* motor */}
      <rect x="40" y="132" width="170" height="112" rx="12" fill="url(#pmp-motor)" stroke="#1e3a8a" strokeWidth="2" />
      {[0, 1, 2, 3, 4].map((i) => (
        <line key={i} x1={66 + i * 30} y1="142" x2={66 + i * 30} y2="234" stroke="#93c5fd" strokeWidth="3" strokeLinecap="round" opacity="0.5" />
      ))}
      <rect x="26" y="150" width="16" height="76" rx="4" fill="#1e3a8a" />

      {/* coupling guard */}
      <rect x="210" y="166" width="46" height="44" rx="4" fill="#f59e0b" stroke="#b45309" />
      {[0, 1, 2].map((i) => (
        <line key={i} x1={220 + i * 12} y1="170" x2={220 + i * 12} y2="206" stroke="#b45309" strokeWidth="2" />
      ))}

      {/* bearing housing */}
      {alarm && <rect x="250" y="150" width="68" height="72" rx="10" fill="none" stroke={HEALTH_STYLE[health].hex} strokeWidth="6" opacity="0.4" />}
      <rect x="256" y="156" width="56" height="60" rx="8" fill="url(#pmp-metal)" stroke="#64748b" strokeWidth="2" />

      {/* volute casing + impeller */}
      <circle cx="372" cy="186" r="62" fill="url(#pmp-metal)" stroke="#64748b" strokeWidth="2" />
      <circle cx="372" cy="186" r="40" fill="#cbd5e1" stroke="#94a3b8" />
      <g className={running ? "motion-safe:animate-[spin_1.6s_linear_infinite]" : undefined} style={{ transformBox: "fill-box", transformOrigin: "center" }}>
        <circle cx="372" cy="186" r="34" fill="none" stroke="none" />
        {[0, 60, 120, 180, 240, 300].map((deg) => {
          const a = (deg * Math.PI) / 180;
          return <path key={deg} d={`M372 186 Q${372 + 22 * Math.cos(a + 0.6)} ${186 + 22 * Math.sin(a + 0.6)} ${372 + 32 * Math.cos(a)} ${186 + 32 * Math.sin(a)}`} fill="none" stroke="#475569" strokeWidth="3" strokeLinecap="round" />;
        })}
        <circle cx="372" cy="186" r="6" fill="#475569" />
      </g>

      {/* baseplate */}
      <rect x="64" y="244" width="22" height="20" fill="#475569" />
      <rect x="164" y="244" width="22" height="20" fill="#475569" />
      <rect x="350" y="246" width="44" height="18" fill="#475569" />
      <rect x="20" y="262" width="490" height="20" rx="6" fill="#334155" />
    </svg>
  );
}
