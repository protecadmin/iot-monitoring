using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWindowedCalculations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CalculationWindowSeconds",
                table: "DeviceTypeParameters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WindowSeconds",
                table: "CalculationRules",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalculationWindowSeconds",
                table: "DeviceTypeParameters");

            migrationBuilder.DropColumn(
                name: "WindowSeconds",
                table: "CalculationRules");
        }
    }
}
