using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class FarmPendingReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByTesterId",
                table: "Farms",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingReviewSince",
                table: "Farms",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Farms_CreatedByTesterId",
                table: "Farms",
                column: "CreatedByTesterId");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_PendingReviewSince",
                table: "Farms",
                column: "PendingReviewSince");

            migrationBuilder.AddForeignKey(
                name: "FK_Farms_AspNetUsers_CreatedByTesterId",
                table: "Farms",
                column: "CreatedByTesterId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Farms_AspNetUsers_CreatedByTesterId",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_CreatedByTesterId",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_PendingReviewSince",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "CreatedByTesterId",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "PendingReviewSince",
                table: "Farms");
        }
    }
}
