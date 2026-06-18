using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class SyncTesterClientIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MachineTests_ClientId",
                table: "MachineTests");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TesterId_ClientId",
                table: "MachineTests",
                columns: new[] { "TesterId", "ClientId" },
                unique: true,
                filter: "[ClientId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TesterId_ClientId",
                table: "MachineTests");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_ClientId",
                table: "MachineTests",
                column: "ClientId",
                unique: true,
                filter: "[ClientId] IS NOT NULL");
        }
    }
}
