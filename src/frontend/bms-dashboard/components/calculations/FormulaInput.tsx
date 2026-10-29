"use client";

import { useRef } from "react";
import { inputClass } from "@/components/ui/Modal";
import { FORMULA_KEY_PATTERN, FORMULA_MAX_LENGTH } from "@/lib/calculations";

export interface FormulaParameter {
  key: string;
  label: string;
  unit: string | null;
}

/**
 * A formula over a reading's parameters, e.g. voltage * current * powerFactor / 1000.
 * Clicking a parameter inserts its key at the cursor. Validation is the
 * form's job (validateFormula), shown under the field.
 */
export function FormulaInput({
  value,
  onChange,
  parameters,
  placeholder = "voltage * current * powerFactor / 1000",
}: {
  value: string;
  onChange: (value: string) => void;
  /** Parameters the formula may use — measured ones only, never Derived. */
  parameters: FormulaParameter[];
  placeholder?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  // A formula names parameters by bare key, so a key with spaces
  // ("3 phase power factor") can't be told apart from arithmetic.
  const usable = parameters.filter((p) => FORMULA_KEY_PATTERN.test(p.key));
  const unusable = parameters.filter((p) => !FORMULA_KEY_PATTERN.test(p.key));

  function insert(key: string) {
    const el = inputRef.current;
    const start = el?.selectionStart ?? value.length;
    const end = el?.selectionEnd ?? value.length;
    const before = value.slice(0, start);
    const after = value.slice(end);
    const pad = before && !/[\s(]$/.test(before) ? " " : "";
    const next = `${before}${pad}${key}${after}`;
    onChange(next);
    requestAnimationFrame(() => {
      const caret = before.length + pad.length + key.length;
      el?.focus();
      el?.setSelectionRange(caret, caret);
    });
  }

  return (
    <div className="space-y-1.5">
      <input
        ref={inputRef}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        maxLength={FORMULA_MAX_LENGTH}
        spellCheck={false}
        className={`${inputClass} font-mono`}
      />
      {usable.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {usable.map((p) => (
            <button
              key={p.key}
              type="button"
              onClick={() => insert(p.key)}
              title={`${p.label}${p.unit ? ` (${p.unit})` : ""} — click to insert`}
              className="px-1.5 py-0.5 rounded border border-border bg-muted text-[11px] font-mono text-foreground hover:border-primary hover:text-primary"
            >
              {p.key}
            </button>
          ))}
        </div>
      )}
      <p className="text-xs text-muted-foreground">
        Use parameter keys, numbers, + − × ÷ (as + - * /) and parentheses. Calculated for each reading that carries every parameter used — and only when the
        device doesn&apos;t send the value itself.
      </p>
      {unusable.length > 0 && (
        <p className="text-xs text-muted-foreground">
          Can&apos;t be used in a formula (key isn&apos;t a single word): <span className="font-mono">{unusable.map((p) => p.key).join(", ")}</span>
        </p>
      )}
    </div>
  );
}
