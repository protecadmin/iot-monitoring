"use client";

import { Plus, Trash2 } from "lucide-react";
import type { DeviceTypeParameterDto, DeviceValueConversionDto } from "@/types/api";

/** One row as typed — the formula stays text until saved. */
export interface ConversionRow {
  parameterKey: string;
  formula: string;
}

const round = (v: number) => Number(v.toPrecision(12)) + 0; // + 0 turns -0 into 0

/** A stored (raw − Offset) × Scale written back as the formula a person would type. */
function toFormula({ offset, scale }: DeviceValueConversionDto): string {
  const shifted = offset === 0 ? "x" : offset > 0 ? `x - ${offset}` : `x + ${-offset}`;
  if (scale === 1) return shifted;
  return offset === 0 ? `x * ${scale}` : `(${shifted}) * ${scale}`;
}

export const toRows = (conversions: DeviceValueConversionDto[] | null | undefined): ConversionRow[] =>
  (conversions ?? []).map((c) => ({ parameterKey: c.parameterKey, formula: toFormula(c) }));

/**
 * Evaluates a formula in x — numbers, x, + − × ÷ (or * /) and brackets only.
 * Throws on anything else.
 */
function evaluate(formula: string, x: number): number {
  const tokens = formula.replace(/×/g, "*").replace(/÷/g, "/").replace(/−/g, "-").match(/\d+\.?\d*|\.\d+|[xX]|[-+*/()]|\S/g) ?? [];
  let i = 0;
  const peek = () => tokens[i];
  const expr = (): number => {
    let v = term();
    while (peek() === "+" || peek() === "-") v = tokens[i++] === "+" ? v + term() : v - term();
    return v;
  };
  const term = (): number => {
    let v = factor();
    while (peek() === "*" || peek() === "/") v = tokens[i++] === "*" ? v * factor() : v / factor();
    return v;
  };
  const factor = (): number => {
    const t = tokens[i++];
    if (t === "-") return -factor();
    if (t === "+") return factor();
    if (t === "x" || t === "X") return x;
    if (t === "(") {
      const v = expr();
      if (tokens[i++] !== ")") throw new Error("missing )");
      return v;
    }
    if (t !== undefined && /^[\d.]/.test(t)) return Number(t);
    throw new Error("unexpected");
  };
  const v = expr();
  if (i !== tokens.length) throw new Error("unexpected");
  return v;
}

/**
 * The formula as the stored (raw − Offset) × Scale, or why it can't be one.
 * Only a straight-line formula fits (x / 10 * 100, x * 9 / 5 + 32, …) — checked
 * at a few points, so x * x or 1 / x are refused.
 */
export function formulaToConversion(formula: string): { offset: number; scale: number } | { error: string } {
  if (!formula.trim()) return { error: "Enter a formula, e.g. x / 10 * 100" };
  if (!/[xX]/.test(formula)) return { error: "The formula must use x (the value the device sends)" };
  let f0: number, f1: number;
  try {
    f0 = evaluate(formula, 0);
    f1 = evaluate(formula, 1);
    const scale = f1 - f0;
    for (const x of [2, -3, 1000])
      if (!(Math.abs(evaluate(formula, x) - (f0 + scale * x)) <= 1e-9 * Math.max(1, Math.abs(f0 + scale * x))))
        return { error: "Only a simple scale and shift fits here — x * x or 1 / x can't be stored" };
  } catch {
    return { error: "Use only x, numbers, + − × / and brackets — e.g. x / 10 * 100" };
  }
  const scale = round(f1 - f0);
  if (!Number.isFinite(scale) || !Number.isFinite(f0) || scale === 0) return { error: "Use only x, numbers, + − × / and brackets — e.g. x / 10 * 100" };
  return { offset: round(-f0 / scale), scale };
}

/** The rows as the API takes them, or why they can't be saved. */
export function parseRows(rows: ConversionRow[]): { conversions: DeviceValueConversionDto[]; error?: string } {
  const conversions: DeviceValueConversionDto[] = [];
  for (const r of rows) {
    if (!r.parameterKey) return { conversions, error: "Pick the parameter for every value conversion" };
    if (conversions.some((c) => c.parameterKey.toLowerCase() === r.parameterKey.toLowerCase()))
      return { conversions, error: `${r.parameterKey} has more than one value conversion` };
    const c = formulaToConversion(r.formula);
    if ("error" in c) return { conversions, error: `${r.parameterKey}: ${c.error}` };
    conversions.push({ parameterKey: r.parameterKey, ...c });
  }
  return { conversions };
}

const EXAMPLE_RAW = 10;

/**
 * Unit corrections for an MQTT-direct device that sends a wrong unit, typed
 * as a formula in x and stored as (raw − Offset) × Scale under the same key.
 * A connector device converts in its connector's mapping, so this is never
 * shown for one.
 */
export function ValueConversionsEditor({
  parameters,
  rows,
  onChange,
}: {
  parameters: DeviceTypeParameterDto[];
  rows: ConversionRow[];
  onChange: (rows: ConversionRow[]) => void;
}) {
  const update = (i: number, patch: Partial<ConversionRow>) => onChange(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));

  return (
    <div>
      <p className="block text-xs font-medium text-muted-foreground mb-1">Value conversion (optional)</p>
      <p className="text-xs text-muted-foreground mb-2">
        Only for a device that sends a wrong unit. Write how to turn the value it sends (<span className="font-mono">x</span>) into the right one, e.g.{" "}
        <span className="font-mono">x / 10 * 100</span>, <span className="font-mono">x / 1000</span> (milli → whole) or{" "}
        <span className="font-mono">(x - 32) * 5 / 9</span> (°F → °C). Applies from the next reading on; readings already stored aren&apos;t changed.
      </p>
      {rows.length > 0 && (
        <table className="w-full text-sm mb-2">
          <thead className="text-xs text-muted-foreground">
            <tr>
              <th className="text-left font-medium py-1">Parameter</th>
              <th className="text-left font-medium py-1">Formula</th>
              <th className="text-left font-medium py-1">Example</th>
              <th className="w-6" />
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => {
              const p = parameters.find((x) => x.key.toLowerCase() === r.parameterKey.toLowerCase());
              const c = r.formula.trim() ? formulaToConversion(r.formula) : null;
              const unit = p?.unit ? ` ${p.unit}` : "";
              return (
                <tr key={i}>
                  <td className="py-1 pr-2">
                    <select value={r.parameterKey} onChange={(e) => update(i, { parameterKey: e.target.value })} className="w-full border border-input rounded px-2 py-1 bg-background text-sm">
                      <option value="">Choose…</option>
                      {parameters.map((x) => (
                        <option key={x.key} value={x.key}>
                          {x.label} ({x.key})
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="py-1 pr-2">
                    <input
                      value={r.formula}
                      onChange={(e) => update(i, { formula: e.target.value })}
                      placeholder="x / 10 * 100"
                      className={`w-full border rounded px-2 py-1 bg-background text-sm font-mono ${c && "error" in c ? "border-destructive" : "border-input"}`}
                    />
                  </td>
                  <td className="py-1 pr-2 text-xs whitespace-nowrap">
                    {!c ? (
                      <span className="text-muted-foreground">—</span>
                    ) : "error" in c ? (
                      <span className="text-destructive" title={c.error}>
                        Not a valid formula
                      </span>
                    ) : (
                      <span className="font-mono text-muted-foreground" title="What a reading of 10 would be stored as">
                        sends {EXAMPLE_RAW} → stored {round((EXAMPLE_RAW - c.offset) * c.scale)}
                        {unit}
                      </span>
                    )}
                  </td>
                  <td className="py-1 text-right">
                    <button type="button" onClick={() => onChange(rows.filter((_, j) => j !== i))} className="text-muted-foreground hover:text-destructive" aria-label="Remove conversion">
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      <button
        type="button"
        onClick={() => onChange([...rows, { parameterKey: "", formula: "" }])}
        disabled={parameters.length === 0}
        className="inline-flex items-center gap-1 text-xs text-primary hover:underline disabled:opacity-50"
      >
        <Plus className="h-3.5 w-3.5" /> Add conversion
      </button>
    </div>
  );
}
