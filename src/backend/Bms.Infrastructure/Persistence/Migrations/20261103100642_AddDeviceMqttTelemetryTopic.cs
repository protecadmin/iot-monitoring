using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceMqttTelemetryTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MqttTelemetryTopic",
                table: "Devices",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_MqttTelemetryTopic",
                table: "Devices",
                column: "MqttTelemetryTopic",
                unique: true);

            // The broker grants each company its registered topics, regardless of layout.
            migrationBuilder.Sql("""
                CREATE VIEW "MqttAllowedTopics" AS
                SELECT "Username", 'iot/' || "Username" || '/+/+/#' AS "Topic" FROM "MqttCredentials"
                UNION
                SELECT c."Username", d."MqttTelemetryTopic" FROM "Devices" d
                JOIN "MqttCredentials" c ON c."TenantId" = d."TenantId"
                WHERE d."Connection" = 'MqttDirect' AND d."MqttTelemetryTopic" IS NOT NULL
                UNION
                SELECT 'bms-ingestion', "MqttTelemetryTopic" FROM "Devices"
                WHERE "Connection" = 'MqttDirect' AND "MqttTelemetryTopic" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW "MqttAllowedTopics";""");
            migrationBuilder.DropIndex(
                name: "IX_Devices_MqttTelemetryTopic",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MqttTelemetryTopic",
                table: "Devices");
        }
    }
}
