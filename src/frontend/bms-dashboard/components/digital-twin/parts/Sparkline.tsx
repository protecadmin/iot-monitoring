/** Last-hour trend of the headline reading, with the warning limit as a dashed line. */
export function Sparkline({ values, warning, color, className = "h-12" }: { values: number[]; warning?: number; color: string; className?: string }) {
  // One short line: compact rows give this only ~20px of height, so a wrapped sentence spills into the next row.
  if (values.length < 2)
    return (
      <div title="Trend appears after a few readings." className={`${className} text-[11px] text-muted-foreground flex items-center overflow-hidden whitespace-nowrap text-ellipsis`}>
        Collecting trend…
      </div>
    );

  const W = 300;
  const H = 48;
  const top = Math.max(...values, warning ?? -Infinity) * 1.08;
  const bottom = Math.min(...values, 0);
  const y = (v: number) => H - ((v - bottom) / (top - bottom || 1)) * H;
  const x = (i: number) => (i / (values.length - 1)) * W;
  const line = values.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className={`w-full ${className}`} aria-label="Trend over the last hour">
      <polygon points={`0,${H} ${line} ${W},${H}`} fill={color} opacity="0.12" />
      {warning != null && <line x1="0" x2={W} y1={y(warning)} y2={y(warning)} stroke="#f59e0b" strokeDasharray="4 4" strokeWidth="1.5" vectorEffect="non-scaling-stroke" />}
      <polyline points={line} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" />
      <circle cx={x(values.length - 1)} cy={y(values[values.length - 1])} r="3" fill={color} />
    </svg>
  );
}
