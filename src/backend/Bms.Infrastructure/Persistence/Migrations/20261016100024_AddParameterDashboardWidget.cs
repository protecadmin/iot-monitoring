using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterDashboardWidget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DashboardWidget",
                table: "DeviceTypeParameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Sensible defaults for the readings already in use: the safety and
            // condition readings get a Good / Caution / Danger card, active
            // power a live card. Everything else stays off the dashboard until
            // it's chosen on the Device Types page.
            migrationBuilder.Sql("""
                UPDATE "DeviceTypeParameters" SET "DashboardWidget" = 'SafetyStatus'
                WHERE "Key" IN ('vibration', 'noiseLevel', 'wbgt');
                UPDATE "DeviceTypeParameters" SET "DashboardWidget" = 'LiveValue'
                WHERE "Key" = 'activePower';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DashboardWidget",
                table: "DeviceTypeParameters");
        }
    }
}
