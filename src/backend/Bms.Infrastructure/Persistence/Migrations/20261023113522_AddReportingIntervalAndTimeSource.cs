using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIntervalAndTimeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpectedIntervalSeconds",
                table: "DeviceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsCommands",
                table: "DeviceTypes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeSource",
                table: "Devices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Device");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedIntervalSeconds",
                table: "DeviceTypes");

            migrationBuilder.DropColumn(
                name: "SupportsCommands",
                table: "DeviceTypes");

            migrationBuilder.DropColumn(
                name: "TimeSource",
                table: "Devices");
        }
    }
}
