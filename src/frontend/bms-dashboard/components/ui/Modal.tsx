"use client";

import { useEffect } from "react";
import { X } from "lucide-react";

const SIZE_CLASSES = {
  md: "max-w-md",
  lg: "max-w-2xl",
  xl: "max-w-4xl",
} as const;

export function Modal({
  title,
  onClose,
  children,
  size = "md",
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
  /** "md" (default, ~28rem) for short forms; "lg"/"xl" for forms with many fields, so a 2+ column layout fits without cramping. */
  size?: keyof typeof SIZE_CLASSES;
}) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    // z-[2000]: Leaflet's own panes/controls (see DeviceMap) use z-index
    // values up to 1000 and don't establish their own stacking context, so
    // a lower z-index here gets painted over by the map on pages that show both.
    <div className="fixed inset-0 z-[2000] flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        className={`bg-card text-card-foreground rounded-lg shadow-xl border border-border w-full ${SIZE_CLASSES[size]} max-h-[90vh] overflow-y-auto`}
        onClick={(e) => e.stopPropagation()}
      >
        {/* sticky: without this, scrolling a long form (e.g. New device) scrolls the title and close button out of view along with the fields. */}
        <div className="sticky top-0 z-10 flex items-center justify-between px-5 py-3.5 border-b border-border bg-card">
          <h2 className="text-sm font-semibold text-foreground">{title}</h2>
          <button onClick={onClose} aria-label="Close" className="text-muted-foreground hover:text-foreground rounded-md p-1 hover:bg-muted">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="p-5">{children}</div>
      </div>
    </div>
  );
}

export function FormField({ label, children, error }: { label: string; children: React.ReactNode; error?: string | null }) {
  return (
    <div>
      <label className="block text-xs font-medium text-muted-foreground mb-1">{label}</label>
      {children}
      {error && <p className="text-xs text-destructive mt-1">{error}</p>}
    </div>
  );
}

export const inputClass =
  "w-full text-sm border border-input bg-background text-foreground rounded-md px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-ring focus:border-transparent placeholder:text-muted-foreground/60";
