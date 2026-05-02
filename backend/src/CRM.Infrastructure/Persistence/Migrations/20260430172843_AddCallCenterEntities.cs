using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCallCenterEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConsentCaptured",
                table: "Leads",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LeadSourceId",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredSkillCode",
                table: "Leads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnsweredAt",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "CallRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WrapUpCode",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClockInAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClockOutAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalAvailable = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TotalOnCall = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TotalBreak = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TotalWrapUp = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SkillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Proficiency = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSkills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentStatusLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    FromAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UntilAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RelatedCallRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentStatusLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VerticalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DncEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhoneNormalized = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DncEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CostPerLead = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TcpaConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsentText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TcpaConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WrapUpCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsSale = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsContact = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRetry = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WrapUpCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_AgencyId_UserId_ClockInAt",
                table: "AgentSessions",
                columns: new[] { "AgencyId", "UserId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSkills_UserId_SkillId",
                table: "AgentSkills",
                columns: new[] { "UserId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentStatusLogs_AgencyId_UserId_FromAt",
                table: "AgentStatusLogs",
                columns: new[] { "AgencyId", "UserId", "FromAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_AgencyId_Code",
                table: "Campaigns",
                columns: new[] { "AgencyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DncEntries_AgencyId_PhoneNormalized",
                table: "DncEntries",
                columns: new[] { "AgencyId", "PhoneNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadSources_AgencyId_Code",
                table: "LeadSources",
                columns: new[] { "AgencyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scripts_AgencyId_Name_Version",
                table: "Scripts",
                columns: new[] { "AgencyId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_AgencyId_Code",
                table: "Skills",
                columns: new[] { "AgencyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TcpaConsents_LeadId",
                table: "TcpaConsents",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_WrapUpCodes_AgencyId_Code",
                table: "WrapUpCodes",
                columns: new[] { "AgencyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentSessions");

            migrationBuilder.DropTable(
                name: "AgentSkills");

            migrationBuilder.DropTable(
                name: "AgentStatusLogs");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "DncEntries");

            migrationBuilder.DropTable(
                name: "LeadSources");

            migrationBuilder.DropTable(
                name: "Scripts");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "TcpaConsents");

            migrationBuilder.DropTable(
                name: "WrapUpCodes");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ConsentCaptured",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LeadSourceId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "RequiredSkillCode",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "WrapUpCode",
                table: "CallRecords");
        }
    }
}
