using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeePayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeePayrolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    BasicSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Punctuality = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DailyBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MonthlyCommissions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransportAllowance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpecialAllowance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdvanceSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Docks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WorkingDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PresentDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LeavesApproved = table.Column<int>(type: "INTEGER", nullable: false),
                    LateComing = table.Column<int>(type: "INTEGER", nullable: false),
                    HalfDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsentDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Ncns = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Finalized = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayrolls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrolls_AgencyId_Year_Month",
                table: "EmployeePayrolls",
                columns: new[] { "AgencyId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayrolls_EmployeeId_Year_Month",
                table: "EmployeePayrolls",
                columns: new[] { "EmployeeId", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeePayrolls");
        }
    }
}
