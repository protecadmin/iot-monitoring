using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations;

public partial class IndependentGateways : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Gateways_Sites_SiteId", "Gateways");
        migrationBuilder.AddColumn<string>("GatewayCode", "Gateways", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("Status", "Gateways", maxLength: 24, nullable: false, defaultValue: "Standalone");
        migrationBuilder.AddColumn<string>("FirmwareVersion", "Gateways", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<int>("Rssi", "Gateways", nullable: true);
        migrationBuilder.AddColumn<double>("BatteryPct", "Gateways", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("LastMeasurementAt", "Gateways", nullable: true);
        migrationBuilder.AlterColumn<Guid>("TenantId", "Gateways", nullable: true, oldClrType: typeof(Guid));
        migrationBuilder.AlterColumn<Guid>("SiteId", "Gateways", nullable: true, oldClrType: typeof(Guid));
        migrationBuilder.Sql("UPDATE \"Gateways\" SET \"GatewayCode\" = \"Id\"::text WHERE \"GatewayCode\" IS NULL");
        migrationBuilder.AlterColumn<string>("GatewayCode", "Gateways", maxLength: 128, nullable: false, oldClrType: typeof(string), oldNullable: true);
        migrationBuilder.CreateIndex("IX_Gateways_GatewayCode", "Gateways", "GatewayCode", unique: true);
        migrationBuilder.AddForeignKey("FK_Gateways_Sites_SiteId", "Gateways", "SiteId", "Sites", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Gateways_Sites_SiteId", "Gateways");
        migrationBuilder.DropIndex("IX_Gateways_GatewayCode", "Gateways");
        migrationBuilder.AlterColumn<Guid>("TenantId", "Gateways", nullable: false, oldClrType: typeof(Guid), oldNullable: true);
        migrationBuilder.AlterColumn<Guid>("SiteId", "Gateways", nullable: false, oldClrType: typeof(Guid), oldNullable: true);
        migrationBuilder.AddForeignKey("FK_Gateways_Sites_SiteId", "Gateways", "SiteId", "Sites", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.DropColumn("GatewayCode", "Gateways");
        migrationBuilder.DropColumn("Status", "Gateways");
        migrationBuilder.DropColumn("FirmwareVersion", "Gateways");
        migrationBuilder.DropColumn("Rssi", "Gateways");
        migrationBuilder.DropColumn("BatteryPct", "Gateways");
        migrationBuilder.DropColumn("LastMeasurementAt", "Gateways");
    }
}
