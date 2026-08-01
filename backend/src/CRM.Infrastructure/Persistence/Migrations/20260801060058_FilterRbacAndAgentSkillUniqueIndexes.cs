using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterRbacAndAgentSkillUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RoleModules_RoleId_ModuleId",
                table: "RoleModules");

            migrationBuilder.DropIndex(
                name: "IX_AgentSkills_UserId_SkillId",
                table: "AgentSkills");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RoleModules_RoleId_ModuleId",
                table: "RoleModules",
                columns: new[] { "RoleId", "ModuleId" },
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSkills_UserId_SkillId",
                table: "AgentSkills",
                columns: new[] { "UserId", "SkillId" },
                unique: true,
                filter: "\"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RoleModules_RoleId_ModuleId",
                table: "RoleModules");

            migrationBuilder.DropIndex(
                name: "IX_AgentSkills_UserId_SkillId",
                table: "AgentSkills");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleModules_RoleId_ModuleId",
                table: "RoleModules",
                columns: new[] { "RoleId", "ModuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSkills_UserId_SkillId",
                table: "AgentSkills",
                columns: new[] { "UserId", "SkillId" },
                unique: true);
        }
    }
}
