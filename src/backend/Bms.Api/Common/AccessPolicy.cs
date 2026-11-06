using Bms.Domain;

namespace Bms.Api.Common;

/// <summary>Default-deny API capability matrix. Metadata needed to render readings is read-only.</summary>
public static class AccessPolicy
{
    public static bool Allows(string role, string controller, string action, string method)
    {
        if (!AccessRoles.IsSupported(role)) return false;
        if (role == AccessRoles.SuperAdmin) return true;
        if (controller == "Auth") return true;
        var read = method is "GET" or "HEAD";
        if (controller == "Users") return role == AccessRoles.CompanyAdmin;
        if (controller == "Tenants") return read || (role == AccessRoles.CompanyAdmin && action == "Update");
        if (controller == "Sites") return read || (role == AccessRoles.CompanyAdmin && action == "Update");
        if (controller is "Devices" or "Telemetry" or "Reports" or "TiltMonitoring" or "DeviceBaselines") return read;
        if (controller == "Calculations") return read && action is "Results" or "DeviceCalculations";
        if (controller == "Alerts") return read || (role is AccessRoles.CompanyAdmin or AccessRoles.SiteAdmin && action == "Acknowledge");
        // Device types and effective thresholds are shared display metadata, never configuration writes.
        if (controller == "DeviceTypes") return read && action == "List";
        if (controller == "GlobalAlertRules") return read && action == "Effective";
        return false;
    }
}
