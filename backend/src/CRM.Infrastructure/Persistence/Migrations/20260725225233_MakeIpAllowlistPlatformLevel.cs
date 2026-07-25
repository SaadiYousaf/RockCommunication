using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeIpAllowlistPlatformLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IpAllowlist_Agencies_AgencyId",
                table: "IpAllowlist");

            migrationBuilder.DropIndex(
                name: "IX_IpAllowlist_AgencyId",
                table: "IpAllowlist");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgencyId",
                table: "IpAllowlist",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "AgencyId",
                table: "IpAllowlist",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpAllowlist_AgencyId",
                table: "IpAllowlist",
                column: "AgencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_IpAllowlist_Agencies_AgencyId",
                table: "IpAllowlist",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
