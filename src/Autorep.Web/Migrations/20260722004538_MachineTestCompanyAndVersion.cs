using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class MachineTestCompanyAndVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesClientId",
                table: "MachineTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TestingCompanyId",
                table: "MachineTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "MachineTests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing rows have no stamp, so seed it from the owner's current company — the only
            // definition available retrospectively. From here on the stamp is written once at
            // upload and never re-derived, so a tester changing companies leaves their history
            // behind (see MachineTest.TestingCompanyId).
            migrationBuilder.Sql(@"
                UPDATE t SET t.[TestingCompanyId] = u.[TestingCompanyId]
                FROM [MachineTests] t
                INNER JOIN [AspNetUsers] u ON u.[Id] = t.[TesterId]
                WHERE t.[TestingCompanyId] IS NULL");

            // Version + SupersedesClientId already exist inside PayloadJson for tests written by a
            // device that knew about versioning; lift them into their columns so the company list
            // can hide superseded versions of tests that were re-edited before this migration.
            // JSON_VALUE is lax by default: a legacy payload without these keys yields NULL, and
            // TRY_CONVERT keeps malformed values from failing the migration.
            migrationBuilder.Sql(@"
                UPDATE [MachineTests]
                SET [Version] = COALESCE(TRY_CONVERT(int, JSON_VALUE([PayloadJson], '$.version')), 1),
                    [SupersedesClientId] = TRY_CONVERT(uniqueidentifier, JSON_VALUE([PayloadJson], '$.supersedesId'))
                WHERE [PayloadJson] IS NOT NULL AND ISJSON([PayloadJson]) = 1");

            // Rows with no usable payload keep the ADD COLUMN default of 0; every test is at least v1.
            migrationBuilder.Sql("UPDATE [MachineTests] SET [Version] = 1 WHERE [Version] < 1");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TesterId_SupersedesClientId",
                table: "MachineTests",
                columns: new[] { "TesterId", "SupersedesClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TestingCompanyId_MarkedCompleteAt",
                table: "MachineTests",
                columns: new[] { "TestingCompanyId", "MarkedCompleteAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TesterId_SupersedesClientId",
                table: "MachineTests");

            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TestingCompanyId_MarkedCompleteAt",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "SupersedesClientId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "TestingCompanyId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MachineTests");
        }
    }
}
