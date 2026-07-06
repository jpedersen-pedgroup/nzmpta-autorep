using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class MachineTestUpdatedAtWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "MachineTests",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing rows (including migrated legacy tests) enter the delta stream at their last
            // known write, not at the 0001-01-01 column default.
            migrationBuilder.Sql(
                "UPDATE [MachineTests] SET [UpdatedAt] = COALESCE([MarkedCompleteAt], [CreatedAt])");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TesterId_UpdatedAt",
                table: "MachineTests",
                columns: new[] { "TesterId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TesterId_UpdatedAt",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MachineTests");
        }
    }
}
