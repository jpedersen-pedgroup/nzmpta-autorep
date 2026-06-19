using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyContentAndTesterTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAcceptedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TermsAcceptedLicenceExpiry",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedVersion",
                table: "AspNetUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivacyContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TermsBody = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CollectionNotice = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ReportFooterText = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    PrivacyContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PrivacyStatementUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivacyContent", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrivacyContent");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedLicenceExpiry",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedVersion",
                table: "AspNetUsers");
        }
    }
}
