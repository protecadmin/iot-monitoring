using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorPrefixFraming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CsvFieldCount",
                table: "Connectors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartMarker",
                table: "Connectors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CsvFieldCount",
                table: "Connectors");

            migrationBuilder.DropColumn(
                name: "StartMarker",
                table: "Connectors");
        }
    }
}
