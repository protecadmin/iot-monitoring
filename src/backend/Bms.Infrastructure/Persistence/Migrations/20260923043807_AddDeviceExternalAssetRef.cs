using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceExternalAssetRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalAssetRef",
                table: "Devices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_ExternalAssetRef",
                table: "Devices",
                columns: new[] { "TenantId", "ExternalAssetRef" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_TenantId_ExternalAssetRef",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ExternalAssetRef",
                table: "Devices");
        }
    }
}
