"use client";

import { createContext, useContext, useEffect, useState } from "react";

type Theme = "light" | "dark";

const ThemeContext = createContext<{ theme: Theme; toggle: () => void } | null>(null);

const STORAGE_KEY = "bms.theme";

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setTheme] = useState<Theme>("light");
  // Gates the apply/persist effect below until the stored value has
  // actually been read — without this, that effect ran once on mount with
  // the default "light" state (before the read below had a chance to
  // correct it) and immediately overwrote a real "dark" preference back to
  // "light" in localStorage. It "self-corrected" a render later when the
  // read's setTheme flowed through, but that race meant a refresh could
  // easily end up back on light depending on timing/StrictMode
  // double-invocation — this flag just skips that first, premature run.
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    let stored: string | null = null;
    try {
      stored = window.localStorage.getItem(STORAGE_KEY);
    } catch {
      // localStorage unavailable (private browsing, blocked storage) — fall back to light.
    }
    if (stored === "dark" || stored === "light") setTheme(stored);
    setHydrated(true);
  }, []);

  useEffect(() => {
    if (!hydrated) return;
    document.documentElement.classList.toggle("dark", theme === "dark");
    try {
      window.localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // Non-fatal — the toggle still works for this page load.
    }
  }, [theme, hydrated]);

  return <ThemeContext.Provider value={{ theme, toggle: () => setTheme((t) => (t === "light" ? "dark" : "light")) }}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used within ThemeProvider");
  return ctx;
}
