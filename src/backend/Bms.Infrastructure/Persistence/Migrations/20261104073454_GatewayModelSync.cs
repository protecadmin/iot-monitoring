using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GatewayModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gateways_Sites_SiteId",
                table: "Gateways");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Gateways",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Gateways",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Gateways",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<double>(
                name: "BatteryPct",
                table: "Gateways",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "Gateways",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayCode",
                table: "Gateways",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMeasurementAt",
                table: "Gateways",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rssi",
                table: "Gateways",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Gateways",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Gateways_GatewayCode",
                table: "Gateways",
                column: "GatewayCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Gateways_Sites_SiteId",
                table: "Gateways",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gateways_Sites_SiteId",
                table: "Gateways");

            migrationBuilder.DropIndex(
                name: "IX_Gateways_GatewayCode",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "BatteryPct",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "GatewayCode",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "LastMeasurementAt",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "Rssi",
                table: "Gateways");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Gateways");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Gateways",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Gateways",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Gateways",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddForeignKey(
                name: "FK_Gateways_Sites_SiteId",
                table: "Gateways",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
