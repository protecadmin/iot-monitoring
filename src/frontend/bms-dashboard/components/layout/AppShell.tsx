"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import Image from "next/image";
import { usePathname } from "next/navigation";
import {
  Bell,
  BellRing,
  Box,
  Boxes,
  Building2,
  Calculator,
  ChevronsLeft,
  ChevronsRight,
  Cpu,
  FileText,
  HardDriveDownload,
  KeyRound,
  LayoutDashboard,
  LogOut,
  MapPin,
  Menu,
  Plug,
  ScrollText,
  Send,
  Settings as SettingsIcon,
  ShieldOff,
  Users,
  X,
} from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { hasPermission, type Permission } from "@/lib/auth/permissions";
import { useTenantScope } from "@/lib/tenant/TenantScopeContext";
import { useApi } from "@/hooks/useApi";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { TenantsApi, AlertsApi } from "@/lib/api/resources";

// Four groups: Monitoring (daily operational pages), Calculations (derived
// parameters calculated automatically from raw telemetry), Infrastructure (the
// core Tenant -> Site -> Device hierarchy plus the MQTT credentials that
// let those devices connect — the chain you build out per customer),
// Administration (supporting configuration: device type
// definitions, logins, audit trail — see docs/mqtt/topic-design.md for the
// hierarchy this mirrors).
// `permission` hides the item for roles that lack it (see lib/auth/permissions.ts,
// which mirrors the API's [Authorize(Roles = …)]); opening its URL directly
// shows a "no access" message instead of a page full of 403s.
const NAV_GROUPS: { label: string; items: { href: string; label: string; icon: typeof Cpu; permission?: Permission }[] }[] = [
  {
    label: "Monitoring",
    items: [
      { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
      // Watched like the dashboard — status, latest reading, alerts — by every role.
      { href: "/devices", label: "Devices", icon: Cpu },
      { href: "/digital-twin", label: "Digital Twin", icon: Box },
      { href: "/alerts", label: "Alerts", icon: Bell },
      { href: "/commands", label: "Commands", icon: Send, permission: "technical.view" },
      { href: "/firmware", label: "Firmware", icon: HardDriveDownload, permission: "technical.view" },
      { href: "/reports", label: "Reports", icon: FileText },
    ],
  },
  {
    label: "Rules",
    items: [
      { href: "/calculations/rules", label: "Calculation Rules", icon: Calculator, permission: "technical.view" },
      { href: "/alert-rules", label: "Alert Rules", icon: BellRing, permission: "globalAlertRules.manage" },
    ],
  },
  {
    label: "Infrastructure",
    items: [
      { href: "/tenants", label: "Tenants", icon: Building2, permission: "tenants.manage" },
      { href: "/sites", label: "Sites", icon: MapPin },
      { href: "/mqtt-credentials", label: "MQTT Credentials", icon: KeyRound, permission: "mqttCredentials.manage" },
      { href: "/connectors", label: "Connectors", icon: Plug, permission: "connectors.manage" },
    ],
  },
  {
    label: "Administration",
    items: [
      { href: "/device-types", label: "Device Types", icon: Boxes, permission: "technical.view" },
      { href: "/users", label: "Users", icon: Users, permission: "users.manage" },
      { href: "/audit-log", label: "Audit Log", icon: ScrollText, permission: "auditLog.view" },
    ],
  },
];

const ALL_ITEMS = NAV_GROUPS.flatMap((g) => g.items).concat([{ href: "/settings", label: "Settings", icon: SettingsIcon }]);

function isActive(pathname: string | null, href: string) {
  return pathname === href || !!pathname?.startsWith(href + "/");
}

function pageTitleFor(pathname: string | null): string {
  const match = ALL_ITEMS.find((i) => isActive(pathname, i.href));
  if (match) return match.label;
  if (pathname?.startsWith("/devices/")) return "Device Details";
  return "Pro-Tec Monitoring";
}

/**
 * Only a SuperAdmin sees this — a tenant-scoped user is pinned to their
 * own tenant both here and server-side (see EffectiveTenantFilter), so
 * there is nothing for them to choose between.
 */
function TenantSwitcher({ collapsed }: { collapsed: boolean }) {
  const { selectedTenantId, setSelectedTenantId, canSwitchTenant } = useTenantScope();
  const pathname = usePathname();
  // Refetches on every navigation (not just on mount) — otherwise a tenant
  // created after the shell first loaded (e.g. from the Tenants page) never
  // shows up here until a full page reload, since canSwitchTenant itself
  // never changes for an already-logged-in SuperAdmin.
  const tenantsQuery = useApi(() => (canSwitchTenant ? TenantsApi.list() : Promise.resolve(undefined)), [canSwitchTenant, pathname]);

  if (!canSwitchTenant || collapsed) return null;

  return (
    <div className="px-3 py-3 border-b border-sidebar-border">
      <label className="block text-xs font-medium text-muted-foreground uppercase tracking-wide mb-1 px-1">Tenant</label>
      <select
        value={selectedTenantId ?? ""}
        onChange={(e) => setSelectedTenantId(e.target.value || null)}
        className="w-full text-sm border border-input rounded-md px-2 py-1.5 bg-background text-foreground"
      >
        <option value="">All tenants</option>
        {tenantsQuery.data?.items.map((t) => (
          <option key={t.id} value={t.id}>
            {t.name}
          </option>
        ))}
      </select>
    </div>
  );
}

function NotificationBell() {
  const { effectiveTenantId } = useTenantScope();
  const query = useApi(() => AlertsApi.list({ tenantId: effectiveTenantId, status: "Open", pageSize: 200 }), [effectiveTenantId]);
  const [liveCount, setLiveCount] = useState<number | null>(null);
  const count = liveCount ?? query.data?.totalCount ?? 0;

  useRealtime({
    AlertCreated: () => setLiveCount((prev) => (prev ?? query.data?.totalCount ?? 0) + 1),
    AlertResolved: () => setLiveCount((prev) => Math.max(0, (prev ?? query.data?.totalCount ?? 0) - 1)),
  });

  return (
    <Link href="/alerts" className="relative p-2 rounded-md hover:bg-muted text-muted-foreground hover:text-foreground" title="Open alerts">
      <Bell className="h-4.5 w-4.5" />
      {count > 0 && (
        <span className="absolute top-1 right-1 min-w-[16px] h-[16px] px-0.5 rounded-full bg-destructive text-destructive-foreground text-[10px] leading-[16px] text-center font-medium">
          {count > 99 ? "99+" : count}
        </span>
      )}
    </Link>
  );
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { user, logout } = useAuth();
  const [collapsedSetting, setCollapsed] = useState(false);
  // Phones: the sidebar is a drawer, opened from the header's menu button.
  const [mobileOpen, setMobileOpen] = useState(false);
  // The open drawer always shows labels; "collapsed" is a desktop setting.
  const collapsed = collapsedSetting && !mobileOpen;
  const closeMobile = () => setMobileOpen(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const pageTitle = useMemo(() => pageTitleFor(pathname), [pathname]);
  const blocked = useMemo(() => {
    const item = ALL_ITEMS.find((i) => isActive(pathname, i.href));
    return item?.permission && !hasPermission(user?.role, item.permission) ? item : null;
  }, [pathname, user?.role]);

  return (
    // h-screen + overflow-hidden pins the shell to exactly the viewport
    // height so it never grows with page content — only <main> below
    // scrolls internally, keeping the sidebar and header fixed in place.
    <div className="flex h-screen overflow-hidden bg-background text-foreground">
      {mobileOpen && <div className="fixed inset-0 z-30 bg-black/40 md:hidden" onClick={closeMobile} aria-hidden="true" />}
      <aside
        className={`fixed inset-y-0 left-0 z-40 w-64 shrink-0 border-r border-sidebar-border bg-sidebar text-sidebar-foreground flex flex-col transition-transform duration-200 md:static md:z-auto md:translate-x-0 md:transition-[width] md:duration-150 ${
          mobileOpen ? "translate-x-0" : "-translate-x-full"
        } ${collapsed ? "md:w-16" : "md:w-56"}`}
      >
        <div className={`relative flex shrink-0 items-center justify-center border-b border-sidebar-border py-3 ${collapsed ? "px-0" : "px-4"}`}>
          <Link href="/dashboard" onClick={closeMobile} aria-label="Pro-Tec Geosystems dashboard" className="flex min-w-0 flex-col items-center justify-center gap-1.5 text-center">
            <span className="inline-flex shrink-0 rounded-md bg-white p-1">
              <Image
                src="/logo/protecgeosystems-logo.webp"
                alt="Pro-Tec Geosystems"
                width={1533}
                height={931}
                className={`h-auto ${collapsed ? "w-9" : "w-24"}`}
              />
            </span>
            {!collapsed && <span className="text-xs text-muted-foreground">Smart Monitoring System</span>}
          </Link>
          <button onClick={closeMobile} className="absolute right-2 top-2 md:hidden p-1 rounded-md text-muted-foreground hover:bg-muted hover:text-foreground" aria-label="Close menu">
            <X className="h-5 w-5" />
          </button>
        </div>

        <TenantSwitcher collapsed={collapsed} />

        <nav className="flex-1 py-2 overflow-y-auto overflow-x-hidden [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          {NAV_GROUPS.map((group) => ({ ...group, items: group.items.filter((item) => !item.permission || hasPermission(user?.role, item.permission)) }))
            .filter((group) => group.items.length > 0)
            .map((group) => (
            <div key={group.label} className="mb-3">
              {/* A heading over a single item (a customer's Infrastructure is just Sites) is clutter. */}
              {!collapsed && group.items.length > 1 && <div className="px-4 pt-2 pb-1 text-xs font-semibold text-muted-foreground uppercase tracking-wide">{group.label}</div>}
              {group.items.map((item) => {
                const active = isActive(pathname, item.href);
                const Icon = item.icon;
                return (
                  <Link
                    key={item.href}
                    aria-current={active ? "page" : undefined}
                    href={item.href}
                    onClick={closeMobile}
                    title={collapsed ? item.label : undefined}
                    className={`flex items-center gap-3 px-4 py-2 text-sm mx-0 ${collapsed ? "justify-center px-0" : ""} ${
                      active
                        ? "bg-primary/10 text-primary font-medium border-r-2 border-primary"
                        : "text-muted-foreground hover:bg-muted hover:text-foreground"
                    }`}
                  >
                    <Icon className="h-4 w-4 shrink-0" />
                    {!collapsed && <span className="truncate">{item.label}</span>}
                  </Link>
                );
              })}
            </div>
          ))}
          <div className="border-t border-sidebar-border pt-2">
            <Link
              href="/settings"
              aria-current={isActive(pathname, "/settings") ? "page" : undefined}
              onClick={closeMobile}
              title={collapsed ? "Settings" : undefined}
              className={`flex items-center gap-3 px-4 py-2 text-sm ${collapsed ? "justify-center px-0" : ""} ${
                isActive(pathname, "/settings")
                  ? "bg-primary/10 text-primary font-medium border-r-2 border-primary"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              }`}
            >
              <SettingsIcon className="h-4 w-4 shrink-0" />
              {!collapsed && "Settings"}
            </Link>
          </div>
        </nav>

        <button
          onClick={() => setCollapsed((c) => !c)}
          className="hidden md:flex items-center gap-2 px-4 py-2.5 text-xs text-muted-foreground hover:text-foreground hover:bg-muted border-t border-sidebar-border"
        >
          {collapsed ? <ChevronsRight className="h-4 w-4 mx-auto" /> : (
            <>
              <ChevronsLeft className="h-4 w-4" /> Collapse
            </>
          )}
        </button>
      </aside>

      <div className="flex-1 min-w-0 flex flex-col">
        <header className="h-14 shrink-0 border-b border-border bg-card flex items-center justify-between gap-2 px-3 md:px-6">
          <div className="flex items-center gap-2 min-w-0">
            <button
              onClick={() => setMobileOpen(true)}
              className="md:hidden p-2 -ml-1 rounded-md text-muted-foreground hover:bg-muted hover:text-foreground"
              aria-label="Open menu"
            >
              <Menu className="h-5 w-5" />
            </button>
            <h1 className="text-sm font-semibold text-foreground truncate">{pageTitle}</h1>
          </div>
          <div className="flex items-center gap-1">
            <NotificationBell />
            <div className="relative ml-1">
              <button
                onClick={() => setUserMenuOpen((o) => !o)}
                className="flex items-center gap-2 pl-2 pr-1 py-1 rounded-md hover:bg-muted"
              >
                <div className="h-7 w-7 rounded-full bg-primary text-primary-foreground text-xs font-semibold flex items-center justify-center shrink-0">
                  {(user?.displayName ?? "?").slice(0, 1).toUpperCase()}
                </div>
                <div className="hidden sm:block text-left leading-tight">
                  <div className="text-xs font-medium text-foreground">{user?.displayName}</div>
                  <div className="text-[11px] text-muted-foreground">{user?.role}</div>
                </div>
              </button>
              {userMenuOpen && (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setUserMenuOpen(false)} />
                  <div className="absolute right-0 mt-1 w-48 bg-popover border border-border rounded-md shadow-lg py-1 z-20">
                    <div className="px-3 py-2 border-b border-border">
                      <div className="text-xs font-medium text-foreground truncate">{user?.email}</div>
                      <div className="text-[11px] text-muted-foreground">{user?.role}</div>
                    </div>
                    <button
                      onClick={logout}
                      className="w-full flex items-center gap-2 px-3 py-2 text-sm text-muted-foreground hover:bg-muted hover:text-foreground"
                    >
                      <LogOut className="h-3.5 w-3.5" /> Log out
                    </button>
                  </div>
                </>
              )}
            </div>
          </div>
        </header>

        <main className="flex-1 min-w-0 overflow-y-auto overflow-x-auto">
          <div className="w-full min-w-0 p-3 sm:p-4">
            {blocked ? (
              <div className="text-center py-16 space-y-2">
                <ShieldOff className="h-8 w-8 mx-auto text-muted-foreground" />
                <p className="text-sm font-medium text-foreground">You don&apos;t have access to {blocked.label}</p>
                <p className="text-xs text-muted-foreground">Your role ({user?.role}) can&apos;t open this page. Ask an Admin if you need it.</p>
              </div>
            ) : (
              children
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
