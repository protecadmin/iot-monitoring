using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertCmmsDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CmmsAttempts",
                table: "Alerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CmmsDeliveredAt",
                table: "Alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CmmsDeliveryStatus",
                table: "Alerts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CmmsError",
                table: "Alerts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CmmsReference",
                table: "Alerts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TenantId_CmmsDeliveryStatus",
                table: "Alerts",
                columns: new[] { "TenantId", "CmmsDeliveryStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_TenantId_CmmsDeliveryStatus",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CmmsAttempts",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CmmsDeliveredAt",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CmmsDeliveryStatus",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CmmsError",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CmmsReference",
                table: "Alerts");
        }
    }
}
