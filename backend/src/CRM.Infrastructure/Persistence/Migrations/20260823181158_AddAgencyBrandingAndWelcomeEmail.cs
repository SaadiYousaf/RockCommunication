using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyBrandingAndWelcomeEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WelcomeEmailSentAt",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Agencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderEmail",
                table: "Agencies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WelcomeEmailSentAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "SenderEmail",
                table: "Agencies");
        }
    }
}
