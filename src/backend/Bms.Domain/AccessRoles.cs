namespace Bms.Domain;

public static class AccessRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Executive = "Executive";
    public const string CompanyAdmin = "CompanyAdmin";
    public const string SiteAdmin = "SiteAdmin";
    public static bool IsSupported(string? role) => role is SuperAdmin or Executive or CompanyAdmin or SiteAdmin;
    public static bool IsPlatform(string? role) => role is SuperAdmin or Executive;
}
