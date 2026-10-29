using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives the original catalogue device types the one-line description the
    /// Device Types page shows under each name (WBGT Sensor and Noise Sensor
    /// already have one from AddWbgtAndNoiseDeviceTypes).
    ///
    /// Data, not schema. Only fills a description that is still empty, so text
    /// someone has since written is kept; a type renamed by hand is simply not
    /// matched. Fresh databases get the same text from DbSeeder, since the
    /// types do not exist yet when this runs there.
    /// </summary>
    public partial class AddDeviceTypeDescriptions : Migration
    {
        private static readonly (string Name, string Description)[] Descriptions =
        [
            ("Temperature Sensor", "Ambient or process temperature measurement."),
            ("Humidity Sensor", "Relative humidity monitor with ambient temperature."),
            ("Pressure Sensor", "Barometric or process pressure with ambient conditions."),
            ("Vibration Sensor", "Machine vibration velocity for condition monitoring (ISO 10816)."),
            ("Energy Meter", "Electrical energy, current, voltage and power factor metering."),
            ("Air Quality Sensor", "Indoor air quality monitor — CO2, PM2.5 and VOC."),
            ("Water Level Sensor", "Tank, sump or reservoir water level monitor."),
            ("Environmental Station", "Multi-sensor station for temperature, humidity and pressure."),
            ("Industrial Gateway", "Edge gateway that aggregates field devices and reports its own ambient conditions.")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, description) in Descriptions)
            {
                migrationBuilder.Sql($"""
                    UPDATE "DeviceTypes"
                    SET "Description" = {Quote(description)}, "UpdatedAt" = now()
                    WHERE "Name" = {Quote(name)} AND ("Description" IS NULL OR btrim("Description") = '');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Clears only text this migration wrote, not anything edited since.
            foreach (var (name, description) in Descriptions)
            {
                migrationBuilder.Sql($"""
                    UPDATE "DeviceTypes"
                    SET "Description" = NULL
                    WHERE "Name" = {Quote(name)} AND "Description" = {Quote(description)};
                    """);
            }
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
