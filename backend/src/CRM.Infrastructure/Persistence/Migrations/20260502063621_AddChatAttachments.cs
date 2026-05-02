using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "ChatMessages");
        }
    }
}
