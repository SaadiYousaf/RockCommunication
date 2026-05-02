using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntakePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgeYears",
                table: "Leads",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaritalStatus",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerifierStatus",
                table: "Leads",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LeadApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HealthConditions = table.Column<string>(type: "TEXT", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", nullable: true),
                    Age = table.Column<int>(type: "INTEGER", nullable: true),
                    SmokerStatus = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Carrier = table.Column<string>(type: "TEXT", nullable: true),
                    Plan = table.Column<string>(type: "TEXT", nullable: true),
                    FaceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Premium = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Beneficiary = table.Column<string>(type: "TEXT", nullable: true),
                    SecondBeneficiary = table.Column<string>(type: "TEXT", nullable: true),
                    InitialDraftDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FutureDraftDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    AltPhone = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryDoctor = table.Column<string>(type: "TEXT", nullable: true),
                    Social = table.Column<string>(type: "TEXT", nullable: true),
                    BornIn = table.Column<string>(type: "TEXT", nullable: true),
                    DriversLicense = table.Column<string>(type: "TEXT", nullable: true),
                    Height = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<string>(type: "TEXT", nullable: true),
                    AccountType = table.Column<string>(type: "TEXT", nullable: true),
                    BankName = table.Column<string>(type: "TEXT", nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", nullable: true),
                    RoutingNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CloserStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadApplications_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadApplications_AgencyId_LeadId",
                table: "LeadApplications",
                columns: new[] { "AgencyId", "LeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadApplications_LeadId",
                table: "LeadApplications",
                column: "LeadId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadApplications");

            migrationBuilder.DropColumn(
                name: "AgeYears",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "MaritalStatus",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "VerifierStatus",
                table: "Leads");
        }
    }
}
