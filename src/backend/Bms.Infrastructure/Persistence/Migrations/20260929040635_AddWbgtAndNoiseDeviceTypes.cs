using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the WBGT Sensor (heat stress) and Noise Sensor device types to the
    /// catalogue, with their parameters.
    ///
    /// Data, not schema — and a migration rather than DbSeeder because the
    /// seeder only runs against an empty database, so an existing deployment
    /// would never receive them. Running here means every database gets them
    /// exactly once, including a fresh one (migrations run before the seeder,
    /// which then finds and uses these rows).
    ///
    /// Idempotent against hand-made rows: a type someone already created with
    /// the same name is kept and only gains the parameters it is missing, so a
    /// deployment that added "Noise Sensor" through the UI does not end up with
    /// two of them. Parameter keys match what devices send after mapping —
    /// they are the ingestion allow-list, see DeviceTypeParameter.
    /// </summary>
    public partial class AddWbgtAndNoiseDeviceTypes : Migration
    {
        private static readonly (string Name, string Description, (string Key, string Label, string Unit, double Min, double Max)[] Parameters)[] Types =
        [
            ("WBGT Sensor",
             "Wet Bulb Globe Temperature heat-stress monitor (ISO 7243).",
             [
                 ("wbgt", "WBGT", "°C", 0, 60),
                 ("dryBulb", "Dry-Bulb Temperature", "°C", -20, 70),
                 ("wetBulb", "Natural Wet-Bulb Temperature", "°C", -20, 60),
                 ("globeTemp", "Globe Temperature", "°C", -20, 100),
                 ("humidity", "Humidity", "%", 0, 100)
             ]),
            ("Noise Sensor",
             "Sound level meter for occupational noise exposure.",
             [
                 ("noiseLevel", "Noise Level (LAeq)", "dB(A)", 20, 140),
                 ("noisePeak", "Peak Level (LCpeak)", "dB(C)", 20, 160)
             ])
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, description, parameters) in Types)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO "DeviceTypes" ("Id", "Name", "Description", "CreatedAt", "UpdatedAt")
                    SELECT gen_random_uuid(), {Quote(name)}, {Quote(description)}, now(), now()
                    WHERE NOT EXISTS (SELECT 1 FROM "DeviceTypes" WHERE lower("Name") = lower({Quote(name)}));
                    """);

                foreach (var p in parameters)
                {
                    migrationBuilder.Sql($"""
                        INSERT INTO "DeviceTypeParameters" ("Id", "DeviceTypeId", "Key", "Label", "Unit", "MinValue", "MaxValue", "Kind", "CreatedAt", "UpdatedAt")
                        SELECT gen_random_uuid(), t."Id", {Quote(p.Key)}, {Quote(p.Label)}, {Quote(p.Unit)}, {Num(p.Min)}, {Num(p.Max)}, 'Instantaneous', now(), now()
                        FROM "DeviceTypes" t
                        WHERE lower(t."Name") = lower({Quote(name)})
                          AND NOT EXISTS (
                              SELECT 1 FROM "DeviceTypeParameters" p
                              WHERE p."DeviceTypeId" = t."Id" AND lower(p."Key") = lower({Quote(p.Key)}));
                        """);
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only removes a type nothing uses yet. Once devices, firmware or
            // rules point at it, it is real configuration — rolling back the
            // schema must not delete it (and the FKs would refuse anyway).
            foreach (var (name, _, _) in Types)
            {
                migrationBuilder.Sql($"""
                    DELETE FROM "DeviceTypes" t
                    WHERE t."Name" = {Quote(name)}
                      AND NOT EXISTS (SELECT 1 FROM "Devices" d WHERE d."DeviceTypeId" = t."Id")
                      AND NOT EXISTS (SELECT 1 FROM "Firmwares" f WHERE f."DeviceTypeId" = t."Id")
                      AND NOT EXISTS (SELECT 1 FROM "AlertRules" r WHERE r."DeviceTypeId" = t."Id")
                      AND NOT EXISTS (SELECT 1 FROM "CalculationRules" c WHERE c."DeviceTypeId" = t."Id");
                    """);
            }
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
