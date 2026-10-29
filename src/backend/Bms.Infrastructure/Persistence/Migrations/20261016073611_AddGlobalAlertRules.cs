using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalAlertRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_AlertRules_AlertRuleId",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_AlertRuleId",
                table: "Alerts");

            migrationBuilder.CreateTable(
                name: "GlobalAlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Metric = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Operator = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Threshold = table.Column<double>(type: "double precision", nullable: true),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AppliesByDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalAlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertRuleOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GlobalAlertRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    Threshold = table.Column<double>(type: "double precision", nullable: true),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRuleOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertRuleOverrides_GlobalAlertRules_GlobalAlertRuleId",
                        column: x => x.GlobalAlertRuleId,
                        principalTable: "GlobalAlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRuleOverrides_GlobalAlertRuleId_TenantId_SiteId_Device~",
                table: "AlertRuleOverrides",
                columns: new[] { "GlobalAlertRuleId", "TenantId", "SiteId", "DeviceId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AlertRuleOverrides_TenantId_GlobalAlertRuleId",
                table: "AlertRuleOverrides",
                columns: new[] { "TenantId", "GlobalAlertRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAlertRules_DeviceTypeId",
                table: "GlobalAlertRules",
                column: "DeviceTypeId");

            // Convert the company-wide rules each company kept its own copy of
            // ("High Vibration" three times over) into global rules — one per
            // device type that has the metric, or one for every type for the
            // lifecycle conditions. A rule most companies had applies to all
            // and is switched off where a company had it disabled or missing;
            // a rule only some had applies to selected companies and is
            // switched on for them; where a company's values differed from the
            // most common ones it gets an override with its own. A company
            // with no company-wide rules at all gets the standard ones — the
            // point of global rules.
            // Alerts are re-pointed to the new rules so cooldowns and open
            // alerts carry on. Rules scoped to a site, device or device type
            // stay as that company's own.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE alert_groups ON COMMIT DROP AS
                SELECT lower(r."Name") AS gname, min(r."Name") AS name,
                       lower(r."Metric") AS gmetric, min(r."Metric") AS metric, r."Operator" AS op,
                       mode() WITHIN GROUP (ORDER BY r."Threshold") AS threshold,
                       mode() WITHIN GROUP (ORDER BY r."Severity") AS severity,
                       mode() WITHIN GROUP (ORDER BY r."CooldownSeconds") AS cooldown,
                       -- "Most companies" counts only companies that set up
                       -- company-wide alerting at all, so empty test tenants
                       -- don't turn every standard rule into an opt-in one.
                       2 * count(DISTINCT r."TenantId") FILTER (WHERE r."IsEnabled") > (
                           SELECT count(DISTINCT x."TenantId") FROM "AlertRules" x
                           WHERE x."SiteId" IS NULL AND x."DeviceId" IS NULL AND x."DeviceTypeId" IS NULL) AS applies_default
                FROM "AlertRules" r
                WHERE r."SiteId" IS NULL AND r."DeviceId" IS NULL AND r."DeviceTypeId" IS NULL
                GROUP BY lower(r."Name"), lower(r."Metric"), r."Operator";

                -- Companies that set up company-wide alerting; one without any
                -- gets the standard rules rather than having each switched off.
                CREATE TEMP TABLE alerting_tenants ON COMMIT DROP AS
                SELECT DISTINCT "TenantId" AS id FROM "AlertRules"
                WHERE "SiteId" IS NULL AND "DeviceId" IS NULL AND "DeviceTypeId" IS NULL;

                CREATE TEMP TABLE alert_targets ON COMMIT DROP AS
                SELECT g.*, p."DeviceTypeId" AS type_id, p."Key" AS key, gen_random_uuid() AS id
                FROM alert_groups g
                JOIN "DeviceTypeParameters" p ON lower(p."Key") = g.gmetric
                WHERE g.gmetric NOT IN ('deviceoffline', 'notelemetry')
                UNION ALL
                SELECT g.*, NULL::uuid, CASE g.gmetric WHEN 'deviceoffline' THEN 'DeviceOffline' WHEN 'notelemetry' THEN 'NoTelemetry' ELSE g.metric END, gen_random_uuid()
                FROM alert_groups g
                WHERE g.gmetric IN ('deviceoffline', 'notelemetry')
                   OR NOT EXISTS (SELECT 1 FROM "DeviceTypeParameters" p WHERE lower(p."Key") = g.gmetric);

                INSERT INTO "GlobalAlertRules"
                    ("Id", "Name", "DeviceTypeId", "Metric", "Operator", "Threshold", "Severity", "CooldownSeconds", "IsEnabled", "AppliesByDefault", "CreatedAt", "UpdatedAt")
                SELECT id, name, type_id, key, op, threshold, severity, cooldown, true, applies_default, now(), now()
                FROM alert_targets;

                INSERT INTO "AlertRuleOverrides"
                    ("Id", "TenantId", "GlobalAlertRuleId", "SiteId", "DeviceId", "IsEnabled", "Threshold", "Severity", "CooldownSeconds", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), t."Id", tg.id, NULL, NULL,
                       CASE WHEN tg.applies_default AND (r."Id" IS NULL OR NOT r."IsEnabled") AND t."Id" IN (SELECT id FROM alerting_tenants) THEN false
                            WHEN NOT tg.applies_default AND r."IsEnabled" THEN true END,
                       CASE WHEN r."Threshold" IS DISTINCT FROM tg.threshold THEN r."Threshold" END,
                       CASE WHEN r."Severity" <> tg.severity THEN r."Severity" END,
                       CASE WHEN r."CooldownSeconds" <> tg.cooldown THEN r."CooldownSeconds" END,
                       now(), now()
                FROM alert_targets tg
                CROSS JOIN "Tenants" t
                LEFT JOIN LATERAL (
                    SELECT * FROM "AlertRules" r
                    WHERE r."TenantId" = t."Id" AND r."SiteId" IS NULL AND r."DeviceId" IS NULL AND r."DeviceTypeId" IS NULL
                      AND lower(r."Name") = tg.gname AND lower(r."Metric") = tg.gmetric AND r."Operator" = tg.op
                    ORDER BY r."UpdatedAt" DESC
                    LIMIT 1
                ) r ON true
                WHERE (tg.applies_default AND (r."Id" IS NULL OR NOT r."IsEnabled") AND t."Id" IN (SELECT id FROM alerting_tenants))
                   OR (NOT tg.applies_default AND r."IsEnabled")
                   OR (r."Id" IS NOT NULL AND (r."Threshold" IS DISTINCT FROM tg.threshold
                                             OR r."Severity" <> tg.severity
                                             OR r."CooldownSeconds" <> tg.cooldown));

                UPDATE "Alerts" a
                SET "AlertRuleId" = tg.id
                FROM "AlertRules" r, "Devices" d, alert_targets tg
                WHERE a."AlertRuleId" = r."Id" AND d."Id" = a."DeviceId"
                  AND r."SiteId" IS NULL AND r."DeviceId" IS NULL AND r."DeviceTypeId" IS NULL
                  AND lower(r."Name") = tg.gname AND lower(r."Metric") = tg.gmetric AND r."Operator" = tg.op
                  AND (tg.type_id = d."DeviceTypeId"
                       OR (tg.type_id IS NULL AND NOT EXISTS (
                               SELECT 1 FROM alert_targets x
                               WHERE x.gname = tg.gname AND x.gmetric = tg.gmetric AND x.op = tg.op AND x.type_id = d."DeviceTypeId")));

                DELETE FROM "AlertRules"
                WHERE "SiteId" IS NULL AND "DeviceId" IS NULL AND "DeviceTypeId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRuleOverrides");

            migrationBuilder.DropTable(
                name: "GlobalAlertRules");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_AlertRuleId",
                table: "Alerts",
                column: "AlertRuleId");

            // The foreign key isn't restored: alerts raised by global rules
            // reference rules that no longer exist after this rollback, and the
            // converted company rules can't be rebuilt from the global ones.
        }
    }
}
