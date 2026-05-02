using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionConfigVerticalsEmailOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VerticalId",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredTwoFactorMethod",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AgencyCommissionConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Threshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyCommissionConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailOtpCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Verticals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verticals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCommissionConfigs_AgencyId_RuleName",
                table: "AgencyCommissionConfigs",
                columns: new[] { "AgencyId", "RuleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpCodes_UserId",
                table: "EmailOtpCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Verticals_AgencyId_Name",
                table: "Verticals",
                columns: new[] { "AgencyId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyCommissionConfigs");

            migrationBuilder.DropTable(
                name: "EmailOtpCodes");

            migrationBuilder.DropTable(
                name: "Verticals");

            migrationBuilder.DropColumn(
                name: "VerticalId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PreferredTwoFactorMethod",
                table: "AspNetUsers");
        }
    }
}
