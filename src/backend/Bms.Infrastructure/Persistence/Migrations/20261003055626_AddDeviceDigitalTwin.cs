using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceDigitalTwin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwinAssetName",
                table: "Devices",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwinMountPoint",
                table: "Devices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwinTemplate",
                table: "Devices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwinAssetName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TwinMountPoint",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TwinTemplate",
                table: "Devices");
        }
    }
}
