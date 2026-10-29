"use client";

import { useState } from "react";
import { AlertTriangle } from "lucide-react";
import { Modal } from "@/components/ui/Modal";
import { ApiError } from "@/lib/api/client";

export function ConfirmModal({
  title,
  message,
  confirmLabel = "Delete",
  danger = true,
  onConfirm,
  onClose,
}: {
  title: string;
  message: React.ReactNode;
  confirmLabel?: string;
  /** Styles the confirm button destructive and shows a warning icon — set false for a neutral confirmation. */
  danger?: boolean;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleConfirm() {
    setError(null);
    setBusy(true);
    try {
      await onConfirm();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal title={title} onClose={onClose}>
      <div className="flex gap-3 mb-4">
        {danger && (
          <div className="shrink-0 h-9 w-9 rounded-full bg-destructive/10 text-destructive flex items-center justify-center">
            <AlertTriangle className="h-4.5 w-4.5" />
          </div>
        )}
        <p className="text-sm text-muted-foreground pt-1.5">{message}</p>
      </div>
      {error && <p className="text-xs text-destructive mb-3">{error}</p>}
      <div className="flex gap-2 justify-end">
        <button onClick={onClose} className="text-sm px-3 py-1.5 rounded-md border border-input text-foreground hover:bg-muted">
          Cancel
        </button>
        <button
          disabled={busy}
          onClick={handleConfirm}
          className={`text-sm px-3 py-1.5 rounded-md text-white disabled:opacity-50 ${
            danger ? "bg-destructive hover:bg-destructive/90" : "bg-primary hover:bg-primary/90"
          }`}
        >
          {busy ? "Working..." : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}
