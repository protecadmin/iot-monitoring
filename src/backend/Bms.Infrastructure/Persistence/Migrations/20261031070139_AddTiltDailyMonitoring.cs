using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTiltDailyMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TiltAxes",
                table: "DeviceTypes",
                type: "jsonb",
                nullable: true);

            // Enable the existing inclinometer type only; runtime uses metadata, never names.
            migrationBuilder.Sql("""
                UPDATE "DeviceTypes" t SET "TiltAxes" = jsonb_build_object('X','xDeg','Y','yDeg','Z',
                    CASE WHEN EXISTS (SELECT 1 FROM "DeviceTypeParameters" p WHERE p."DeviceTypeId"=t."Id" AND p."Key"='zDeg') THEN 'zDeg' ELSE NULL END)
                WHERE lower(t."Name")='inclinometer'
                  AND EXISTS (SELECT 1 FROM "DeviceTypeParameters" p WHERE p."DeviceTypeId"=t."Id" AND p."Key"='xDeg')
                  AND EXISTS (SELECT 1 FROM "DeviceTypeParameters" p WHERE p."DeviceTypeId"=t."Id" AND p."Key"='yDeg');
                """);
            migrationBuilder.CreateTable(
                name: "TiltDailyReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CutoffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    X = table.Column<double>(type: "double precision", nullable: true),
                    Y = table.Column<double>(type: "double precision", nullable: true),
                    Z = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiltDailyReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TiltDailyReadings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TiltMonitoringSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosingTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OnboardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BaselineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BaselineX = table.Column<double>(type: "double precision", nullable: true),
                    BaselineY = table.Column<double>(type: "double precision", nullable: true),
                    BaselineZ = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiltMonitoringSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TiltMonitoringSettings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TiltDailyReadings_DeviceId_Date",
                table: "TiltDailyReadings",
                columns: new[] { "DeviceId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TiltMonitoringSettings_DeviceId",
                table: "TiltMonitoringSettings",
                column: "DeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TiltDailyReadings");

            migrationBuilder.DropTable(
                name: "TiltMonitoringSettings");

            migrationBuilder.DropColumn(
                name: "TiltAxes",
                table: "DeviceTypes");
        }
    }
}
