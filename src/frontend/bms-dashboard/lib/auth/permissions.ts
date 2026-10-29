import { useAuth } from "./AuthContext";

/**
 * What each role may do in the dashboard. Every entry mirrors an
 * [Authorize(Roles = …)] on the API — the server is what actually enforces
 * it; this only keeps the UI from offering actions that would come back 403.
 * Change both sides together.
 */
export type Role = "SuperAdmin" | "Admin" | "Engineer" | "Viewer" | "Customer";

const SUPER: Role[] = ["SuperAdmin"];
const ADMIN: Role[] = ["SuperAdmin", "Admin"];
const ENGINEER: Role[] = ["SuperAdmin", "Admin", "Engineer"];
/** Everyone except Customer — Customer only sees monitoring (dashboard, alerts, reports, results, sites, devices' data). */
const STAFF: Role[] = ["SuperAdmin", "Admin", "Engineer", "Viewer"];

export const PERMISSIONS = {
  // Platform-wide — SuperAdmin only
  "tenants.manage": SUPER,
  "deviceTypes.manage": SUPER,
  "mqttCredentials.manage": SUPER,
  "notifications.manage": SUPER,

  // Tenant administration
  "users.manage": ADMIN,
  "auditLog.view": ADMIN,
  "firmware.manage": ADMIN,
  "devices.delete": ADMIN,
  "alertRules.delete": ADMIN,
  // Alert rules defined once for every company — platform-wide.
  "globalAlertRules.manage": SUPER,
  // Connectors: set up by a SuperAdmin; picked from when adding a device.
  "connectors.manage": SUPER,
  "connectors.view": ENGINEER,
  "calculations.delete": ADMIN,

  // Day-to-day engineering
  "devices.edit": ENGINEER,
  "sites.edit": ENGINEER,
  "alerts.handle": ENGINEER,
  "alertRules.edit": ENGINEER,
  "calculations.edit": ENGINEER,
  "commands.send": ENGINEER,
  "configuration.edit": ENGINEER,
  "rawMessages.replay": ENGINEER,

  // Technical views hidden from Customer
  "technical.view": STAFF,
} satisfies Record<string, Role[]>;

export type Permission = keyof typeof PERMISSIONS;

export function hasPermission(role: string | undefined | null, permission: Permission): boolean {
  return !!role && (PERMISSIONS[permission] as string[]).includes(role);
}

/** `const can = useCan(); can("devices.edit")` */
export function useCan() {
  const { user } = useAuth();
  return (permission: Permission) => hasPermission(user?.role, permission);
}
