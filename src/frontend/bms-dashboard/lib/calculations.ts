import type { CalculationRuleDto } from "@/types/api";

/** The API name of the Formula calculation type. */
export const FORMULA_TYPE = "FORMULA";

/**
 * The types that average a level over a period rather than deriving a value
 * from one reading, so they carry a window and publish once per period.
 */
export const WINDOWED_TYPES = ["TIME_WEIGHTED_AVERAGE", "EQUIVALENT_CONTINUOUS_LEVEL"] as const;

export function isWindowedType(type: string): boolean {
  return (WINDOWED_TYPES as readonly string[]).includes(type);
}

/** The API name of the Change from baseline type: reading minus the device's baseline. */
export const BASELINE_TYPE = "BASELINE_DELTA";

/**
 * Which parameters can feed a rule of this type — each handler's
 * IsEligibleSource, by the parameter's declared Kind (never its unit).
 */
export function isEligibleSource(type: string, kind: string): boolean {
  if (type === "TOTALIZER_DELTA") return kind === "Counter";
  if (isWindowedType(type)) return kind === "Instantaneous";
  return kind !== "Derived";
}

/** Mirrors Bms.Domain.Calculations.LevelWindow. */
export const WINDOW_MIN_SECONDS = 10;
export const WINDOW_MAX_SECONDS = 86400;
export const WINDOW_DEFAULT_SECONDS = 300;

/**
 * The periods compliance limits are actually written against, so the common
 * ones are a pick rather than a number to work out. Any value in range is
 * still accepted — these are the ones worth naming.
 */
export const WINDOW_PRESETS: ReadonlyArray<{ seconds: number; label: string }> = [
  { seconds: 60, label: "1 minute" },
  { seconds: 300, label: "5 minutes — NEA boundary noise" },
  { seconds: 900, label: "15 minutes" },
  { seconds: 1800, label: "30 minutes" },
  { seconds: 3600, label: "1 hour — NEA boundary noise" },
  { seconds: 14400, label: "4 hours" },
  { seconds: 28800, label: "8 hours — occupational exposure, work shift" },
  { seconds: 43200, label: "12 hours — NEA boundary noise" },
  { seconds: 86400, label: "24 hours" },
];

/** "8 hours", "5 minutes", "45s" — for showing a window on a rule. */
export function formatWindow(seconds: number): string {
  const preset = WINDOW_PRESETS.find((p) => p.seconds === seconds);
  if (preset) return preset.label.split(" — ")[0];
  if (seconds % 3600 === 0) return `${seconds / 3600} hours`;
  if (seconds % 60 === 0) return `${seconds / 60} minutes`;
  return `${seconds}s`;
}

/** What a rule reads, for display: a formula's text, or its one source parameter over its window. */
export function ruleInputs(rule: Pick<CalculationRuleDto, "expression" | "sourceParameterKey" | "windowSeconds">): string {
  if (rule.expression) return rule.expression;
  return rule.windowSeconds ? `${rule.sourceParameterKey} over ${formatWindow(rule.windowSeconds)}` : rule.sourceParameterKey;
}

/**
 * Parameter keys a formula names, each once. A quick client-side read for
 * hints only — the API parses the formula properly and is the one that
 * rejects an invalid one.
 */
export function formulaVariables(formula: string): string[] {
  const seen = new Map<string, string>();
  for (const match of formula.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) {
    const lower = match[0].toLowerCase();
    if (!seen.has(lower)) seen.set(lower, match[0]);
  }
  return Array.from(seen.values());
}

// Mirrors CalculationsController: RuleKeyPattern and OutputKeyPattern.
export const RULE_KEY_PATTERN = /^[a-z0-9][a-z0-9-]{1,62}$/;
export const OUTPUT_KEY_PATTERN = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;
/** A key a formula can name: one word, as the API's formula parser reads identifiers. */
export const FORMULA_KEY_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/;
export const FORMULA_MAX_LENGTH = 256;

/**
 * Checks a formula the way the API's FormulaExpression parser does — numbers,
 * parameter keys, + - * /, unary minus and parentheses — plus that every key
 * is a measured parameter in scope and the formula doesn't use its own output.
 * Returns the first problem in words, or null when the formula is valid.
 */
export function validateFormula(formula: string, usableKeys: string[], outputKey: string): string | null {
  const text = formula.trim();
  if (!text) return "Enter a formula, e.g. voltage * current * powerFactor / 1000.";
  if (text.length > FORMULA_MAX_LENGTH) return `A formula can be at most ${FORMULA_MAX_LENGTH} characters.`;

  type Token = { kind: "num" | "key" | "op" | "open" | "close"; text: string; at: number };
  const tokens: Token[] = [];
  for (let i = 0; i < text.length; ) {
    const c = text[i];
    if (/\s/.test(c)) {
      i++;
    } else if (/[0-9.]/.test(c)) {
      const start = i;
      while (i < text.length && /[0-9.]/.test(text[i])) i++;
      const literal = text.slice(start, i);
      if (!/^(\d+\.?\d*|\.\d+)$/.test(literal)) return `"${literal}" is not a number (position ${start + 1}).`;
      tokens.push({ kind: "num", text: literal, at: start });
    } else if (/[A-Za-z_]/.test(c)) {
      const start = i;
      while (i < text.length && /[A-Za-z0-9_]/.test(text[i])) i++;
      tokens.push({ kind: "key", text: text.slice(start, i), at: start });
    } else if ("+-*/".includes(c)) {
      tokens.push({ kind: "op", text: c, at: i++ });
    } else if (c === "(") {
      tokens.push({ kind: "open", text: c, at: i++ });
    } else if (c === ")") {
      tokens.push({ kind: "close", text: c, at: i++ });
    } else {
      return `"${c}" isn't allowed (position ${i + 1}) — use only + - * / and parentheses.`;
    }
  }

  // Structure: an operand (number, key or opening parenthesis, optionally
  // after a unary minus) must follow every operator, and an operator or
  // closing parenthesis every operand.
  let expectOperand = true;
  let depth = 0;
  for (const t of tokens) {
    if (expectOperand) {
      if (t.kind === "op" && t.text === "-") continue;
      if (t.kind === "open") {
        depth++;
        continue;
      }
      if (t.kind === "num" || t.kind === "key") {
        expectOperand = false;
        continue;
      }
      return t.kind === "close"
        ? `Empty or unfinished parentheses at position ${t.at + 1}.`
        : `"${t.text}" at position ${t.at + 1} needs a value or parameter before it.`;
    }
    if (t.kind === "op") {
      expectOperand = true;
    } else if (t.kind === "close") {
      if (--depth < 0) return `Unmatched ")" at position ${t.at + 1}.`;
    } else {
      return `Missing an operator (+ - * /) before "${t.text}" at position ${t.at + 1}.`;
    }
  }
  if (expectOperand) return "The formula ends with an operator — finish it with a value or parameter.";
  if (depth > 0) return `Missing ${depth} closing parenthes${depth === 1 ? "is" : "es"}.`;

  const keys = formulaVariables(text);
  if (keys.length === 0) return "Use at least one parameter — a formula of numbers alone never changes.";
  const usable = new Set(usableKeys.map((k) => k.toLowerCase()));
  const unknown = keys.filter((k) => !usable.has(k.toLowerCase()));
  if (unknown.length > 0)
    return `Not a measured parameter of the selected device type: ${unknown.join(", ")}. Pick from the parameters below.`;
  if (outputKey && keys.some((k) => k.toLowerCase() === outputKey.toLowerCase())) return `A formula can't use its own output (${outputKey}).`;
  return null;
}
