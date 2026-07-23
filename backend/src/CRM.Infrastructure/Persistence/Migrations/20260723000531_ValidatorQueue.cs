using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValidatorQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierApproved",
                table: "Sales",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CoverageApproved",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "Sales",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanApproved",
                table: "Sales",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PremiumApproved",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidatorStatus",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_AgencyId_ValidatorStatus",
                table: "Sales",
                columns: new[] { "AgencyId", "ValidatorStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_AgencyId_ValidatorStatus",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CarrierApproved",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CoverageApproved",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PlanApproved",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PremiumApproved",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ValidatorStatus",
                table: "Sales");
        }
    }
}
