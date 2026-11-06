using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSiteAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve legacy roles and users until their access is explicitly reviewed.
            migrationBuilder.Sql("""
                INSERT INTO "Roles" ("Id", "Name", "CreatedAt", "UpdatedAt") VALUES
                ('4453d042-8585-4e9a-9e85-f87f7fab0001', 'SuperAdmin', now(), now()),
                ('4453d042-8585-4e9a-9e85-f87f7fab0002', 'Executive', now(), now()),
                ('4453d042-8585-4e9a-9e85-f87f7fab0003', 'CompanyAdmin', now(), now()),
                ('4453d042-8585-4e9a-9e85-f87f7fab0004', 'SiteAdmin', now(), now())
                ON CONFLICT ("Name") DO NOTHING;
                """);
            migrationBuilder.CreateTable(
                name: "UserSiteAccess",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSiteAccess", x => new { x.UserId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_UserSiteAccess_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSiteAccess_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSiteAccess_SiteId",
                table: "UserSiteAccess",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSiteAccess");
        }
    }
}
