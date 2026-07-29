using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AgentCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    OfficialEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Designation = table.Column<int>(type: "INTEGER", nullable: false),
                    CallCenterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Cnic = table.Column<string>(type: "TEXT", nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    MaritalStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GuardianPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    GuardianRelationship = table.Column<int>(type: "INTEGER", nullable: true),
                    CurrentAddress = table.Column<string>(type: "TEXT", nullable: true),
                    PermanentAddress = table.Column<string>(type: "TEXT", nullable: true),
                    DateOfJoining = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EmploymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportingToEmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WorkHours = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BankAccountTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "TEXT", nullable: true),
                    BankIban = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PhotoKey = table.Column<string>(type: "TEXT", nullable: true),
                    IdCardFrontKey = table.Column<string>(type: "TEXT", nullable: true),
                    IdCardBackKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_AgencyId",
                table: "Employees",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_AgencyId_AgentCode",
                table: "Employees",
                columns: new[] { "AgencyId", "AgentCode" },
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
