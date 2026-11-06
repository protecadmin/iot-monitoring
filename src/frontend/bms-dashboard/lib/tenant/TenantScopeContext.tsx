"use client";

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { useAuth } from "@/lib/auth/AuthContext";
import { hasPermission } from "@/lib/auth/permissions";

const STORAGE_KEY = "bms.tenantScope";

interface TenantScopeValue {
  /**
   * The tenant every scoped query should filter by, or undefined for
   * "across all tenants". For a tenant-scoped user this is always their
   * own tenant regardless of anything stored — only a SuperAdmin (who has
   * no tenant of their own) can actually change scope, and the API
   * enforces the same rule server-side via EffectiveTenantFilter.
   */
  effectiveTenantId: string | undefined;
  /** What the SuperAdmin picked: null means "All tenants". Meaningless for scoped users. */
  selectedTenantId: string | null;
  setSelectedTenantId: (tenantId: string | null) => void;
  canSwitchTenant: boolean;
}

const TenantScopeContext = createContext<TenantScopeValue | undefined>(undefined);

export function TenantScopeProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const canSwitchTenant = hasPermission(user?.role, "tenantScope.switch");
  const [selectedTenantId, setSelected] = useState<string | null>(null);

  useEffect(() => {
    if (!canSwitchTenant) return;
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (stored) setSelected(stored);
  }, [canSwitchTenant]);

  const setSelectedTenantId = useCallback((tenantId: string | null) => {
    setSelected(tenantId);
    if (tenantId) window.localStorage.setItem(STORAGE_KEY, tenantId);
    else window.localStorage.removeItem(STORAGE_KEY);
  }, []);

  const effectiveTenantId = canSwitchTenant ? (selectedTenantId ?? undefined) : (user?.tenantId ?? undefined);

  return (
    <TenantScopeContext.Provider value={{ effectiveTenantId, selectedTenantId, setSelectedTenantId, canSwitchTenant }}>
      {children}
    </TenantScopeContext.Provider>
  );
}

export function useTenantScope(): TenantScopeValue {
  const ctx = useContext(TenantScopeContext);
  if (!ctx) throw new Error("useTenantScope must be used within TenantScopeProvider");
  return ctx;
}
