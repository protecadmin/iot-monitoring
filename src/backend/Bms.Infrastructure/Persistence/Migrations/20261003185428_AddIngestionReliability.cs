using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestionReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "RawDeviceMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeSeriesWriteAttempts",
                table: "RawDeviceMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TimeSeriesWrittenAt",
                table: "RawDeviceMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Rows from before this migration have no record of whether their
            // InfluxDB write succeeded; treat them as written, or the first
            // backfill pass would re-send the entire history.
            migrationBuilder.Sql(
                "UPDATE \"RawDeviceMessages\" SET \"TimeSeriesWrittenAt\" = \"ReceivedAt\" WHERE \"ProcessingStatus\" = 'Persisted';");

            migrationBuilder.CreateIndex(
                name: "IX_RawDeviceMessages_DeviceId_SequenceNumber",
                table: "RawDeviceMessages",
                columns: new[] { "DeviceId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawDeviceMessages_TimeSeriesPending",
                table: "RawDeviceMessages",
                column: "ReceivedAt",
                filter: "\"ProcessingStatus\" = 'Persisted' AND \"TimeSeriesWrittenAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RawDeviceMessages_DeviceId_DedupKey_Persisted",
                table: "RawDeviceMessages",
                columns: new[] { "DeviceId", "DedupKey" },
                unique: true,
                filter: "\"ProcessingStatus\" = 'Persisted' AND \"DedupKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RawDeviceMessages_DeviceId_SequenceNumber",
                table: "RawDeviceMessages");

            migrationBuilder.DropIndex(
                name: "IX_RawDeviceMessages_TimeSeriesPending",
                table: "RawDeviceMessages");

            migrationBuilder.DropIndex(
                name: "UX_RawDeviceMessages_DeviceId_DedupKey_Persisted",
                table: "RawDeviceMessages");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "RawDeviceMessages");

            migrationBuilder.DropColumn(
                name: "TimeSeriesWriteAttempts",
                table: "RawDeviceMessages");

            migrationBuilder.DropColumn(
                name: "TimeSeriesWrittenAt",
                table: "RawDeviceMessages");
        }
    }
}
