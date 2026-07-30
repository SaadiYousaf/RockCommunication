using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollLineAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AbsentDaysAmount",
                table: "EmployeePayrolls",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HalfDaysAmount",
                table: "EmployeePayrolls",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LateComingAmount",
                table: "EmployeePayrolls",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NcnsAmount",
                table: "EmployeePayrolls",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsentDaysAmount",
                table: "EmployeePayrolls");

            migrationBuilder.DropColumn(
                name: "HalfDaysAmount",
                table: "EmployeePayrolls");

            migrationBuilder.DropColumn(
                name: "LateComingAmount",
                table: "EmployeePayrolls");

            migrationBuilder.DropColumn(
                name: "NcnsAmount",
                table: "EmployeePayrolls");
        }
    }
}
