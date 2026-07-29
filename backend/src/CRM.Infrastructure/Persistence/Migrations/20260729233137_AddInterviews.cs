using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Interviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InterviewDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CandidateName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cnic = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PositionAppliedFor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Experience = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExpectedSalary = table.Column<decimal>(type: "TEXT", nullable: true),
                    AbleToJoinOn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionOffered = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SalaryOffered = table.Column<decimal>(type: "TEXT", nullable: true),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TrainingScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_AgencyId_Status",
                table: "Interviews",
                columns: new[] { "AgencyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Interviews");
        }
    }
}
