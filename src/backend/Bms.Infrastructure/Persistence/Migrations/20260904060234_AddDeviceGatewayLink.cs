using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceGatewayLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GatewayId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_GatewayId",
                table: "Devices",
                column: "GatewayId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Gateways_GatewayId",
                table: "Devices",
                column: "GatewayId",
                principalTable: "Gateways",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Gateways_GatewayId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Devices_GatewayId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "GatewayId",
                table: "Devices");
        }
    }
}
