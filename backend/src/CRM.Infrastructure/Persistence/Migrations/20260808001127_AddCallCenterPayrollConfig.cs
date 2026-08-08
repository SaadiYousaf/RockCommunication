using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCallCenterPayrollConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallCenterPayrollConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CallCenterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LateComingFine = table.Column<decimal>(type: "TEXT", nullable: false),
                    HalfDayFactor = table.Column<decimal>(type: "TEXT", nullable: false),
                    AbsentDayFactor = table.Column<decimal>(type: "TEXT", nullable: false),
                    NcnsFactor = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCenterPayrollConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallCenterPayrollConfigs_CallCenterId",
                table: "CallCenterPayrollConfigs",
                column: "CallCenterId",
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallCenterPayrollConfigs");
        }
    }
}
