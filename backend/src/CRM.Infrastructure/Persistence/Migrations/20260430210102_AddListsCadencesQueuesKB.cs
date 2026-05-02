using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListsCadencesQueuesKB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchesJson",
                table: "Scripts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBranching",
                table: "Scripts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DialMode",
                table: "Campaigns",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PacingRatio",
                table: "Campaigns",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RetryWaitMinutes",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CadenceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CadenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StopReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CadenceEnrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cadences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cadences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredSkillCode = table.Column<string>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Strategy = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MaxWaitSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    OverflowQueueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoicemailAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_InboundQueues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IvrMenus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboundQueueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Greeting = table.Column<string>(type: "TEXT", nullable: false),
                    GreetingAudioUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IvrMenus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    Imported = table.Column<int>(type: "INTEGER", nullable: false),
                    Duplicates = table.Column<int>(type: "INTEGER", nullable: false),
                    DncScrubbed = table.Column<int>(type: "INTEGER", nullable: false),
                    Errors = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadListMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadListMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeadSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LeadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicLeadCaptureEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeadSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CadenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedOrigins = table.Column<string>(type: "TEXT", nullable: true),
                    LeadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicLeadCaptureEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueuedCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboundQueueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromPhone = table.Column<string>(type: "TEXT", nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AbandonedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnsweredByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ProviderCallId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueuedCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoicemailAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoicemailAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoicemailDrops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoicemailAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CallRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoicemailDrops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CadenceSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CadenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    StepKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DelayMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    StopIfContacted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CadenceSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CadenceSteps_Cadences_CadenceId",
                        column: x => x.CadenceId,
                        principalTable: "Cadences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IvrOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IvrMenuId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DigitOrSpeech = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", nullable: false),
                    ActionTargetId = table.Column<string>(type: "TEXT", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgencyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IvrOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IvrOptions_IvrMenus_IvrMenuId",
                        column: x => x.IvrMenuId,
                        principalTable: "IvrMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CadenceEnrollments_AgencyId_NextRunAt_Status",
                table: "CadenceEnrollments",
                columns: new[] { "AgencyId", "NextRunAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CadenceEnrollments_CadenceId_LeadId",
                table: "CadenceEnrollments",
                columns: new[] { "CadenceId", "LeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cadences_AgencyId_Name",
                table: "Cadences",
                columns: new[] { "AgencyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CadenceSteps_CadenceId",
                table: "CadenceSteps",
                column: "CadenceId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundQueues_AgencyId_Name",
                table: "InboundQueues",
                columns: new[] { "AgencyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IvrOptions_IvrMenuId",
                table: "IvrOptions",
                column: "IvrMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_AgencyId_Category",
                table: "KnowledgeArticles",
                columns: new[] { "AgencyId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_AgencyId_Slug",
                table: "KnowledgeArticles",
                columns: new[] { "AgencyId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadImportBatches_AgencyId_LeadListId_CreatedAt",
                table: "LeadImportBatches",
                columns: new[] { "AgencyId", "LeadListId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadListMemberships_LeadListId_LeadId",
                table: "LeadListMemberships",
                columns: new[] { "LeadListId", "LeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadLists_AgencyId_Name",
                table: "LeadLists",
                columns: new[] { "AgencyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicLeadCaptureEndpoints_Slug",
                table: "PublicLeadCaptureEndpoints",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueuedCalls_AgencyId_InboundQueueId_Status_EnteredAt",
                table: "QueuedCalls",
                columns: new[] { "AgencyId", "InboundQueueId", "Status", "EnteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VoicemailAssets_AgencyId_Name",
                table: "VoicemailAssets",
                columns: new[] { "AgencyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoicemailDrops_AgencyId_LeadId_CreatedAt",
                table: "VoicemailDrops",
                columns: new[] { "AgencyId", "LeadId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CadenceEnrollments");

            migrationBuilder.DropTable(
                name: "CadenceSteps");

            migrationBuilder.DropTable(
                name: "InboundQueues");

            migrationBuilder.DropTable(
                name: "IvrOptions");

            migrationBuilder.DropTable(
                name: "KnowledgeArticles");

            migrationBuilder.DropTable(
                name: "LeadImportBatches");

            migrationBuilder.DropTable(
                name: "LeadListMemberships");

            migrationBuilder.DropTable(
                name: "LeadLists");

            migrationBuilder.DropTable(
                name: "PublicLeadCaptureEndpoints");

            migrationBuilder.DropTable(
                name: "QueuedCalls");

            migrationBuilder.DropTable(
                name: "VoicemailAssets");

            migrationBuilder.DropTable(
                name: "VoicemailDrops");

            migrationBuilder.DropTable(
                name: "Cadences");

            migrationBuilder.DropTable(
                name: "IvrMenus");

            migrationBuilder.DropColumn(
                name: "BranchesJson",
                table: "Scripts");

            migrationBuilder.DropColumn(
                name: "IsBranching",
                table: "Scripts");

            migrationBuilder.DropColumn(
                name: "DialMode",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "PacingRatio",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "RetryWaitMinutes",
                table: "Campaigns");
        }
    }
}
