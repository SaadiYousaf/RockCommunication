using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftReflections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftReflections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Learned = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DidJustice = table.Column<bool>(type: "INTEGER", nullable: true),
                    JusticeNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CallCenterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftReflections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReflections_AgencyId_ShiftDate",
                table: "ShiftReflections",
                columns: new[] { "AgencyId", "ShiftDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReflections_UserId_ShiftDate",
                table: "ShiftReflections",
                columns: new[] { "UserId", "ShiftDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftReflections");
        }
    }
}
