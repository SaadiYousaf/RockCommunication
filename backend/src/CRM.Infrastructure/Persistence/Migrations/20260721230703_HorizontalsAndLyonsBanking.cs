using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HorizontalsAndLyonsBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountLast4",
                table: "Sales",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Sales",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankRoutingNumber",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LyonsReference",
                table: "Sales",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Horizontals",
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
                    table.PrimaryKey("PK_Horizontals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Horizontals_AgencyId_Name",
                table: "Horizontals",
                columns: new[] { "AgencyId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Horizontals");

            migrationBuilder.DropColumn(
                name: "BankAccountLast4",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BankRoutingNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LyonsReference",
                table: "Sales");
        }
    }
}
