using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfIndexesAndCallRecordUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ScheduledCallbacks_AgencyId_AssignedUserId_Completed",
                table: "ScheduledCallbacks",
                columns: new[] { "AgencyId", "AssignedUserId", "Completed" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AgencyId_AssignedUserId",
                table: "Leads",
                columns: new[] { "AgencyId", "AssignedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_AgencyId_OccurredAt",
                table: "LeadActivities",
                columns: new[] { "AgencyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_AgencyId_AgentUserId_InitiatedAt",
                table: "CallRecords",
                columns: new[] { "AgencyId", "AgentUserId", "InitiatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_AgencyId_InitiatedAt",
                table: "CallRecords",
                columns: new[] { "AgencyId", "InitiatedAt" });

            // Collapse any pre-existing duplicate (Provider, ProviderCallId) rows created before the
            // unique index existed — soft-delete all but the earliest per group so the CREATE UNIQUE
            // INDEX below (filtered on IsDeleted = 0) cannot fail on legacy data.
            migrationBuilder.Sql(@"
                UPDATE CallRecords SET IsDeleted = 1
                WHERE IsDeleted = 0
                  AND Id NOT IN (
                    SELECT MIN(Id) FROM CallRecords
                    WHERE IsDeleted = 0
                    GROUP BY Provider, ProviderCallId
                  );");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_Provider_ProviderCallId",
                table: "CallRecords",
                columns: new[] { "Provider", "ProviderCallId" },
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledCallbacks_AgencyId_AssignedUserId_Completed",
                table: "ScheduledCallbacks");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Leads_AgencyId_AssignedUserId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_LeadActivities_AgencyId_OccurredAt",
                table: "LeadActivities");

            migrationBuilder.DropIndex(
                name: "IX_CallRecords_AgencyId_AgentUserId_InitiatedAt",
                table: "CallRecords");

            migrationBuilder.DropIndex(
                name: "IX_CallRecords_AgencyId_InitiatedAt",
                table: "CallRecords");

            migrationBuilder.DropIndex(
                name: "IX_CallRecords_Provider_ProviderCallId",
                table: "CallRecords");
        }
    }
}
