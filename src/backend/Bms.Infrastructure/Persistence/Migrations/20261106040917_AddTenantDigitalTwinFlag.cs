using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDigitalTwinFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigitalTwinEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigitalTwinEnabled",
                table: "Tenants");
        }
    }
}
