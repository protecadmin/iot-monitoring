using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the standard "Never Connected" rule (all device types, 24 h,
    /// Warning) to an installation that already has its global rules. A fresh
    /// database is left alone: its global rules table is still empty here,
    /// and DbSeeder creates the full standard set, this rule included, after
    /// migrations — inserting it now would make the seeder skip the rest.
    /// </summary>
    public partial class AddNeverConnectedRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "GlobalAlertRules"
                    ("Id", "Name", "DeviceTypeId", "Metric", "Operator", "Threshold", "Severity", "CooldownSeconds", "IsEnabled", "AppliesByDefault", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), 'Never Connected', NULL, 'NeverConnected', 'GreaterThan', 86400, 'Warning', 3600, true, true, now(), now()
                WHERE EXISTS (SELECT 1 FROM "GlobalAlertRules")
                  AND NOT EXISTS (SELECT 1 FROM "GlobalAlertRules" WHERE "Metric" = 'NeverConnected');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AlertRuleOverrides" WHERE "GlobalAlertRuleId" IN (SELECT "Id" FROM "GlobalAlertRules" WHERE "Metric" = 'NeverConnected');
                DELETE FROM "GlobalAlertRules" WHERE "Metric" = 'NeverConnected';
                """);
        }
    }
}
