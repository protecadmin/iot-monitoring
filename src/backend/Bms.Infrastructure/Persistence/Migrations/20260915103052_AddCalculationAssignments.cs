using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignmentMode",
                table: "CalculationRules",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Automatic");

            migrationBuilder.CreateTable(
                name: "DeviceCalculationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCalculationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCalculationRules_CalculationRules_CalculationRuleId",
                        column: x => x.CalculationRuleId,
                        principalTable: "CalculationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceCalculationRules_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCalculationRules_CalculationRuleId",
                table: "DeviceCalculationRules",
                column: "CalculationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCalculationRules_DeviceId_CalculationRuleId",
                table: "DeviceCalculationRules",
                columns: new[] { "DeviceId", "CalculationRuleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCalculationRules");

            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "CalculationRules");
        }
    }
}
