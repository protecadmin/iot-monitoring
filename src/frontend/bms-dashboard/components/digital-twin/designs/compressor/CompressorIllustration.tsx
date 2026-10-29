import type { IllustrationProps } from "../../types";
import { HEALTH_STYLE } from "../../limits";

/** Air compressor: receiver tank, discharge pipe with pressure gauge, and an electric motor carrying the vibration sensor. */
export function CompressorIllustration({ health, running }: IllustrationProps) {
  const alarm = health === "warning" || health === "critical";
  return (
    <svg viewBox="0 0 520 320" className="w-full h-auto" role="img" aria-label="Air compressor">
      <defs>
        <linearGradient id="cmp-metal" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#e2e8f0" />
          <stop offset="1" stopColor="#94a3b8" />
        </linearGradient>
        <linearGradient id="cmp-motor" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#3b82f6" />
          <stop offset="1" stopColor="#1e40af" />
        </linearGradient>
      </defs>

      {/* floor shadow */}
      <ellipse cx="260" cy="302" rx="250" ry="10" fill="#0f172a" opacity="0.08" />

      {/* discharge pipe + pressure gauge */}
      <rect x="150" y="52" width="18" height="82" fill="#94a3b8" stroke="#64748b" />
      <rect x="168" y="70" width="200" height="18" fill="#94a3b8" stroke="#64748b" />
      <rect x="350" y="88" width="18" height="54" fill="#94a3b8" stroke="#64748b" />
      <circle cx="159" cy="38" r="22" fill="#ffffff" stroke="#475569" strokeWidth="3" />
      <path d="M144 44 A16 16 0 0 1 174 44" fill="none" stroke="#cbd5e1" strokeWidth="3" />
      <line x1="159" y1="38" x2="168" y2="28" stroke="#dc2626" strokeWidth="2.5" strokeLinecap="round" />
      <circle cx="159" cy="38" r="3" fill="#475569" />

      {/* receiver tank */}
      <rect x="30" y="130" width="300" height="130" rx="65" fill="url(#cmp-metal)" stroke="#64748b" strokeWidth="2" />
      <rect x="70" y="150" width="220" height="10" rx="5" fill="#ffffff" opacity="0.45" />

      {/* motor */}
      {alarm && <rect x="333" y="133" width="164" height="134" rx="16" fill="none" stroke={HEALTH_STYLE[health].hex} strokeWidth="6" opacity="0.35" />}
      <rect x="340" y="140" width="150" height="120" rx="12" fill="url(#cmp-motor)" stroke="#1e3a8a" strokeWidth="2" />
      {[0, 1, 2, 3].map((i) => (
        <line key={i} x1="478" y1={156 + i * 26} x2="478" y2={170 + i * 26} stroke="#93c5fd" strokeWidth="3" strokeLinecap="round" opacity="0.6" />
      ))}
      <circle cx="402" cy="200" r="40" fill="none" stroke="#bfdbfe" strokeWidth="2" strokeDasharray="6 5" />
      <g
        className={running ? "motion-safe:animate-[spin_2.5s_linear_infinite]" : undefined}
        style={{ transformBox: "fill-box", transformOrigin: "center" }}
      >
        <circle cx="402" cy="200" r="34" fill="none" stroke="none" />
        {[0, 120, 240].map((deg) => (
          <line
            key={deg}
            x1="402"
            y1="200"
            x2={402 + 30 * Math.cos(((deg - 90) * Math.PI) / 180)}
            y2={200 + 30 * Math.sin(((deg - 90) * Math.PI) / 180)}
            stroke="#eff6ff"
            strokeWidth="3"
            strokeLinecap="round"
          />
        ))}
        <circle cx="402" cy="200" r="5" fill="#eff6ff" />
      </g>

      {/* feet + skid */}
      <rect x="70" y="258" width="22" height="24" fill="#475569" />
      <rect x="250" y="258" width="22" height="24" fill="#475569" />
      <rect x="20" y="280" width="480" height="18" rx="9" fill="#334155" />
    </svg>
  );
}
