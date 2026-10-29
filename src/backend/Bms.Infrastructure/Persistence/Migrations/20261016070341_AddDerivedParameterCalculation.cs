using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedParameterCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationExpression",
                table: "DeviceTypeParameters",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CalculationMaxDelta",
                table: "DeviceTypeParameters",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationSourceKey",
                table: "DeviceTypeParameters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationType",
                table: "DeviceTypeParameters",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // A Derived parameter already calculated by a device-type-wide,
            // automatic rule takes that rule's calculation as its definition,
            // so saving the parameter later keeps every tenant's rule in step
            // instead of losing the formula. Where tenants differ, the most
            // recently updated rule wins.
            migrationBuilder.Sql("""
                UPDATE "DeviceTypeParameters" AS p
                SET "CalculationType" = r."Type",
                    "CalculationExpression" = r."Expression",
                    "CalculationSourceKey" = NULLIF(r."SourceParameterKey", ''),
                    "CalculationMaxDelta" = r."MaxDelta"
                FROM (
                    SELECT DISTINCT ON ("DeviceTypeId", lower("OutputKey"))
                           "DeviceTypeId", "OutputKey", "Type", "Expression", "SourceParameterKey", "MaxDelta"
                    FROM "CalculationRules"
                    WHERE "DeviceTypeId" IS NOT NULL AND "SiteId" IS NULL AND "DeviceId" IS NULL
                      AND "AssignmentMode" = 'Automatic'
                    ORDER BY "DeviceTypeId", lower("OutputKey"), "UpdatedAt" DESC
                ) AS r
                WHERE p."DeviceTypeId" = r."DeviceTypeId"
                  AND lower(p."Key") = lower(r."OutputKey")
                  AND p."Kind" = 'Derived';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalculationExpression",
                table: "DeviceTypeParameters");

            migrationBuilder.DropColumn(
                name: "CalculationMaxDelta",
                table: "DeviceTypeParameters");

            migrationBuilder.DropColumn(
                name: "CalculationSourceKey",
                table: "DeviceTypeParameters");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                table: "DeviceTypeParameters");
        }
    }
}
