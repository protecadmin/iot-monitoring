import { useAuth } from "./AuthContext";

export type Role = "SuperAdmin" | "Executive" | "CompanyAdmin" | "SiteAdmin";
const SUPER: Role[] = ["SuperAdmin"];
const COMPANY: Role[] = ["SuperAdmin", "CompanyAdmin"];
const BUSINESS: Role[] = ["SuperAdmin", "Executive", "CompanyAdmin", "SiteAdmin"];

export const PERMISSIONS = {
  "monitoring.read": BUSINESS,
  "tenants.read": ["SuperAdmin", "Executive"],
  "tenants.manage": SUPER,
  "tenantScope.switch": ["SuperAdmin", "Executive"],
  "settings.view": COMPANY,
  "deviceTypes.manage": SUPER,
  "mqttCredentials.manage": SUPER,
  "notifications.manage": SUPER,
  "users.manage": COMPANY,
  "auditLog.view": SUPER,
  "firmware.manage": SUPER,
  "devices.delete": SUPER,
  "alertRules.delete": SUPER,
  "globalAlertRules.manage": SUPER,
  "connectors.manage": SUPER,
  "connectors.view": SUPER,
  "calculations.delete": SUPER,
  "devices.edit": SUPER,
  "sites.create": SUPER,
  "sites.edit": COMPANY,
  "alerts.handle": ["SuperAdmin", "CompanyAdmin", "SiteAdmin"],
  "alerts.resolve": SUPER,
  "alertRules.edit": SUPER,
  "calculations.edit": SUPER,
  "commands.send": SUPER,
  "configuration.edit": SUPER,
  "rawMessages.replay": SUPER,
  "technical.view": SUPER,
} satisfies Record<string, Role[]>;
export type Permission = keyof typeof PERMISSIONS;
export function hasPermission(role: string | undefined | null, permission: Permission): boolean {
  return !!role && (PERMISSIONS[permission] as string[]).includes(role);
}
export function roleLabel(role: string | undefined | null): string {
  return ({ SuperAdmin: "SuperAdmin", Executive: "Executive", CompanyAdmin: "Company Admin", SiteAdmin: "Site Admin" } as Record<string, string>)[role ?? ""] ?? `${role ?? "Unknown"} (review required)`;
}
export function roleBadgeClass(role: string): string {
  return ({ SuperAdmin: "bg-emerald-950 text-white", Executive: "bg-orange-100 text-orange-800", CompanyAdmin: "bg-emerald-100 text-emerald-800", SiteAdmin: "bg-teal-100 text-teal-800" } as Record<string, string>)[role] ?? "bg-muted text-muted-foreground";
}
export function useCan() {
  const { user } = useAuth();
  return (permission: Permission) => hasPermission(user?.role, permission);
}
