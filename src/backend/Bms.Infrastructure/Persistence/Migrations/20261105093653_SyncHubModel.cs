using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncHubModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    HubId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    AppVersion = table.Column<string>(type: "text", nullable: true),
                    AgentVersion = table.Column<string>(type: "text", nullable: true),
                    SecurityMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Rssi = table.Column<int>(type: "integer", nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    ConfigRevision = table.Column<int>(type: "integer", nullable: false),
                    ConfigHash = table.Column<string>(type: "text", nullable: true),
                    DisplayMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RotationSeconds = table.Column<int>(type: "integer", nullable: true),
                    WelcomeSeconds = table.Column<int>(type: "integer", nullable: true),
                    HistoryRange = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMeasurementAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastConfigSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DesiredConfigJson = table.Column<string>(type: "text", nullable: true),
                    ReportedConfigJson = table.Column<string>(type: "text", nullable: true),
                    LastStatusJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hubs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hubs_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Hubs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hubs_HubId",
                table: "Hubs",
                column: "HubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hubs_SiteId",
                table: "Hubs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Hubs_TenantId",
                table: "Hubs",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hubs");
        }
    }
}
