using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class MachineTestNextTestDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "NextTestDate",
                table: "MachineTests",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TestingCompanyId_NextTestDate",
                table: "MachineTests",
                columns: new[] { "TestingCompanyId", "NextTestDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TestingCompanyId_NextTestDate",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "NextTestDate",
                table: "MachineTests");
        }
    }
}
