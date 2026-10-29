using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePayloadMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_PayloadMappings_PayloadMappingId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "PayloadMappingVersions");

            migrationBuilder.DropTable(
                name: "PayloadMappings");

            migrationBuilder.DropIndex(
                name: "IX_Devices_PayloadMappingId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PayloadMappingId",
                table: "Devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayloadMappingId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayloadMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayloadMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayloadMappingVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FieldMapJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MapperKey = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayloadMappingVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayloadMappingVersions_PayloadMappings_PayloadMappingId",
                        column: x => x.PayloadMappingId,
                        principalTable: "PayloadMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PayloadMappingId",
                table: "Devices",
                column: "PayloadMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_PayloadMappingVersions_PayloadMappingId_Version",
                table: "PayloadMappingVersions",
                columns: new[] { "PayloadMappingId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_PayloadMappings_PayloadMappingId",
                table: "Devices",
                column: "PayloadMappingId",
                principalTable: "PayloadMappings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
