using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Connection",
                table: "Devices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "MqttDirect");

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectorId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InstalledOn",
                table: "Devices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MountingLocation",
                table: "Devices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SensorId",
                table: "Devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DefaultDeviceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Framing = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FixedLength = table.Column<int>(type: "integer", nullable: true),
                    EndCharacter = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    MaxMessageBytes = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CsvSeparator = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CsvOptionalPrefix = table.Column<bool>(type: "boolean", nullable: false),
                    PairSeparator = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    KeyValueSeparator = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    BinaryHeaderHex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BinaryHeaderOffset = table.Column<int>(type: "integer", nullable: false),
                    BinaryChecksum = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DeviceCodePrefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Mappings = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorServiceStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MessagesAccepted = table.Column<long>(type: "bigint", nullable: false),
                    MessagesRejected = table.Column<long>(type: "bigint", nullable: false),
                    MessagesUnknown = table.Column<long>(type: "bigint", nullable: false),
                    MessagesPublished = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ConnectorsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorServiceStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorCaptures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sample = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    SampleIsHex = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorCaptures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorCaptures_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorUnknownDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SensorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MessageCount = table.Column<long>(type: "bigint", nullable: false),
                    LastSample = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorUnknownDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorUnknownDevices_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorUnmappedKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageCount = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastValue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorUnmappedKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorUnmappedKeys_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ConnectorId_SensorId",
                table: "Devices",
                columns: new[] { "ConnectorId", "SensorId" },
                unique: true,
                filter: "\"ConnectorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorCaptures_ConnectorId_CapturedAt",
                table: "ConnectorCaptures",
                columns: new[] { "ConnectorId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Connectors_DeviceCodePrefix",
                table: "Connectors",
                column: "DeviceCodePrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connectors_Protocol_Port",
                table: "Connectors",
                columns: new[] { "Protocol", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorUnknownDevices_ConnectorId_SensorId",
                table: "ConnectorUnknownDevices",
                columns: new[] { "ConnectorId", "SensorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorUnmappedKeys_ConnectorId_Source",
                table: "ConnectorUnmappedKeys",
                columns: new[] { "ConnectorId", "Source" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Connectors_ConnectorId",
                table: "Devices",
                column: "ConnectorId",
                principalTable: "Connectors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Connectors_ConnectorId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "ConnectorCaptures");

            migrationBuilder.DropTable(
                name: "ConnectorServiceStatuses");

            migrationBuilder.DropTable(
                name: "ConnectorUnknownDevices");

            migrationBuilder.DropTable(
                name: "ConnectorUnmappedKeys");

            migrationBuilder.DropTable(
                name: "Connectors");

            migrationBuilder.DropIndex(
                name: "IX_Devices_ConnectorId_SensorId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Connection",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ConnectorId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "InstalledOn",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MountingLocation",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "SensorId",
                table: "Devices");
        }
    }
}
