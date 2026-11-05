using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GatewayTenantNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Gateways_TenantId",
                table: "Gateways",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gateways_Tenants_TenantId",
                table: "Gateways",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gateways_Tenants_TenantId",
                table: "Gateways");

            migrationBuilder.DropIndex(
                name: "IX_Gateways_TenantId",
                table: "Gateways");
        }
    }
}
