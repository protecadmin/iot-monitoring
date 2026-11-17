"use client";
import { useState, type ReactNode } from "react";
import { Check, CheckCircle2, Copy, XCircle, type LucideIcon } from "lucide-react";

export const date = (v?: string | null) => v ? new Date(v).toLocaleString() : "Unavailable";
export const age = (v: string | undefined | null, now: number) => v ? Math.max(0, (now - new Date(v).getTime()) / 1000) : null;
export const duration = (s: number | null) => s == null || !Number.isFinite(s) ? "—" : s < 60 ? `${Math.floor(s)}s` : s < 3600 ? `${Math.floor(s / 60)}m` : `${Math.floor(s / 3600)}h`;
export const stateLabels: Record<string, string> = { awaiting_snapshot: "Verifying snapshot", timed_out: "Timed out", publish_failed: "Publish failed" };
export function Badge({ state, children }: { state: string; children?: ReactNode }) {
  const tone = ["Online", "applied", "succeeded", "live"].includes(state) ? "good" : ["rejected", "Offline", "failed", "publish_failed"].includes(state) ? "bad" : ["pending", "received", "accepted", "awaiting_snapshot", "Stale", "timed_out"].includes(state) ? "wait" : "neutral";
  return <span className={`mqtt-badge ${tone}`}>{tone === "good" ? <CheckCircle2 size={12} /> : tone === "bad" ? <XCircle size={12} /> : <span className="mqtt-dot" />}{children ?? stateLabels[state] ?? state.replaceAll("_", " ")}</span>;
}
export function Panel({ icon: Icon, title, subtitle, extra, children }: { icon: LucideIcon; title: string; subtitle: string; extra?: ReactNode; children: ReactNode }) {
  return <section className="mqtt-panel"><header><Icon size={21} /><div><h2>{title}</h2><p>{subtitle}</p></div>{extra && <span className="mqtt-panel-extra">{extra}</span>}</header><div className="mqtt-panel-body">{children}</div></section>;
}
export function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState(false);
  const label = error ? "Clipboard unavailable" : copied ? "Copied" : "Copy to clipboard";
  return <button type="button" className="mqtt-copy" title={label} aria-label={label} onClick={async () => { try { await navigator.clipboard.writeText(text); setCopied(true); setTimeout(() => setCopied(false), 1800); } catch { setError(true); } }}>{copied ? <Check size={14} /> : <Copy size={14} />}</button>;
}
export function JsonView({ value }: { value: unknown }) {
  const json = typeof value === "string" ? (() => { try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; } })() : JSON.stringify(value, null, 2);
  const [expanded, setExpanded] = useState(false);
  if (!json) return <div className="mqtt-empty">No payload received yet.</div>;
  return <div className={`mqtt-json ${expanded ? "expanded" : ""}`}><div className="mqtt-json-tools"><button type="button" onClick={() => setExpanded(!expanded)}>{expanded ? "Collapse" : "Expand"}</button><CopyButton text={json} /></div><pre>{json.split("\n").map((line, i) => <div key={i}><span className="mqtt-line-number">{i + 1}</span><code>{line.split(/("(?:[^"\\]|\\.)*"\s*:|"(?:[^"\\]|\\.)*"|\btrue\b|\bfalse\b|\bnull\b|\b\d+(?:\.\d+)?\b)/g).map((token, j) => <span key={j} className={token.endsWith(":") ? "json-key" : token.startsWith('"') ? "json-string" : /^(true|false|null|\d)/.test(token) ? "json-value" : ""}>{token}</span>)}</code></div>)}</pre></div>;
}
