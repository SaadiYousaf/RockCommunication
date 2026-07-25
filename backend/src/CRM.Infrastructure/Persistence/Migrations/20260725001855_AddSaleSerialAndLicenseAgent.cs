using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleSerialAndLicenseAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LicenseAgentUserId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SaleNumber",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastSaleNumber",
                table: "Agencies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill per-agency serials BEFORE creating the unique index (otherwise the
            // all-zero defaults collide). Number every existing sale — including soft-deleted
            // ones — so the sequence is stable and the live subset stays unique per agency.
            migrationBuilder.Sql(@"
                UPDATE Sales
                SET SaleNumber = seq.rn
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY AgencyId ORDER BY SoldAt, Id) AS rn
                    FROM Sales
                ) AS seq
                WHERE Sales.Id = seq.Id;");

            // Seed each agency's allocator to its highest issued number so new sales continue on.
            migrationBuilder.Sql(@"
                UPDATE Agencies
                SET LastSaleNumber = (
                    SELECT COALESCE(MAX(s.SaleNumber), 0) FROM Sales s WHERE s.AgencyId = Agencies.Id
                );");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_AgencyId_SaleNumber",
                table: "Sales",
                columns: new[] { "AgencyId", "SaleNumber" },
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_AgencyId_SaleNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LicenseAgentUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SaleNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LastSaleNumber",
                table: "Agencies");
        }
    }
}
