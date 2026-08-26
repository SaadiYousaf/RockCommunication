using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChargedBackAt",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierAdvancingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Carrier = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    AdvancedMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAdvancingRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAdvancingRules_Carrier",
                table: "CarrierAdvancingRules",
                column: "Carrier",
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierAdvancingRules");

            migrationBuilder.DropColumn(
                name: "ChargedBackAt",
                table: "Sales");
        }
    }
}
