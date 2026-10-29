using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "DeviceTypeParameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Instantaneous");

            migrationBuilder.CreateTable(
                name: "CalculationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceParameterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutputKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutputName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OutputUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MaxDelta = table.Column<double>(type: "double precision", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalculationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSourceValue = table.Column<double>(type: "double precision", nullable: false),
                    LastSourceTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastOutputValue = table.Column<double>(type: "double precision", nullable: true),
                    LastOutputTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalculationStates_CalculationRules_CalculationRuleId",
                        column: x => x.CalculationRuleId,
                        principalTable: "CalculationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalculationStates_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalculationRules_TenantId_IsEnabled",
                table: "CalculationRules",
                columns: new[] { "TenantId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_CalculationRules_TenantId_Key",
                table: "CalculationRules",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalculationStates_CalculationRuleId_DeviceId",
                table: "CalculationStates",
                columns: new[] { "CalculationRuleId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalculationStates_DeviceId",
                table: "CalculationStates",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculationStates");

            migrationBuilder.DropTable(
                name: "CalculationRules");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "DeviceTypeParameters");
        }
    }
}
