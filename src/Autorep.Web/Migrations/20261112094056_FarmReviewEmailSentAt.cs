using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class FarmReviewEmailSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewEmailSentAt",
                table: "Farms",
                type: "datetimeoffset",
                nullable: true);

            // Farms already under review were emailed to their reviewers the moment they were added
            // (the old FarmReviewNotifier). Count that as their email, so the first check after the
            // deploy doesn't send a burst about farms the admins have already heard about.
            migrationBuilder.Sql(
                "UPDATE [Farms] SET [ReviewEmailSentAt] = [PendingReviewSince] WHERE [PendingReviewSince] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewEmailSentAt",
                table: "Farms");
        }
    }
}
