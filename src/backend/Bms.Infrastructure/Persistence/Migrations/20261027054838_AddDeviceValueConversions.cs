using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceValueConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValueConversions",
                table: "Devices",
                type: "jsonb",
                nullable: true);

            // Existing devices start with no conversions — an empty list, not
            // null, so they load like a device saved after this migration.
            migrationBuilder.Sql("""UPDATE "Devices" SET "ValueConversions" = '[]'::jsonb WHERE "ValueConversions" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValueConversions",
                table: "Devices");
        }
    }
}
