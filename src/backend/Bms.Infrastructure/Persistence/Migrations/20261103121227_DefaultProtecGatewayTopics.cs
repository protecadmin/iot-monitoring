using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefaultProtecGatewayTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "UX_Devices_EffectiveMqttTopic" ON "Devices"
                ((COALESCE("MqttTelemetryTopic", 'protec/gateway/' || "DeviceCode" || '/measurement')))
                WHERE "Connection" = 'MqttDirect';

                CREATE OR REPLACE VIEW "MqttAllowedTopics" AS
                SELECT "Username", 'iot/' || "Username" || '/+/+/#' AS "Topic" FROM "MqttCredentials"
                UNION
                SELECT c."Username", COALESCE(d."MqttTelemetryTopic", 'protec/gateway/' || d."DeviceCode" || '/measurement')
                FROM "Devices" d JOIN "MqttCredentials" c ON c."TenantId" = d."TenantId"
                WHERE d."Connection" = 'MqttDirect'
                UNION
                SELECT 'bms-ingestion', COALESCE("MqttTelemetryTopic", 'protec/gateway/' || "DeviceCode" || '/measurement')
                FROM "Devices" WHERE "Connection" = 'MqttDirect';
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX "UX_Devices_EffectiveMqttTopic";
                CREATE OR REPLACE VIEW "MqttAllowedTopics" AS
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
    }
}
