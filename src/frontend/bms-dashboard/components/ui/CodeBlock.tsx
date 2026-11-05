"use client";

import { useState } from "react";
import { Check, Copy } from "lucide-react";

/** Monospace block with a copy button — topics, payloads and shell commands in the device guides. */
export function CodeBlock({ label, value }: { label?: string; value: string }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard blocked (non-secure context) — the text is still selectable.
    }
  }

  return (
    <div>
      {label && <div className="text-[11px] font-medium text-muted-foreground mb-1">{label}</div>}
      <div className="relative group">
        <pre className="text-xs font-mono bg-muted/60 border border-border rounded-md px-3 py-2 pr-10 overflow-x-auto whitespace-pre text-foreground">
          {value}
        </pre>
        <button
          onClick={copy}
          title="Copy"
          aria-label="Copy to clipboard"
          className="absolute top-1.5 right-1.5 p-1 rounded bg-card text-muted-foreground hover:text-foreground hover:bg-background"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      </div>
    </div>
  );
}
