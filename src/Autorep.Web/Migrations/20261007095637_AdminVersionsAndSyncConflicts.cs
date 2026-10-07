using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdminVersionsAndSyncConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorId",
                table: "MachineTests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromClientId",
                table: "MachineTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootClientId",
                table: "MachineTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuccessorStamp",
                table: "MachineTests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Every existing version learns which test it belongs to: an original is its own root,
            // and each later version takes its parent's (within one tester's rows — ClientId space
            // is per tester). Chains are a handful of versions long, so the loop runs a few times.
            migrationBuilder.Sql(@"
                UPDATE [MachineTests] SET [RootClientId] = [ClientId]
                WHERE [ClientId] IS NOT NULL AND [SupersedesClientId] IS NULL;

                DECLARE @linked INT = 1;
                WHILE @linked > 0
                BEGIN
                    UPDATE c SET c.[RootClientId] = p.[RootClientId]
                    FROM [MachineTests] c
                    INNER JOIN [MachineTests] p ON p.[TesterId] = c.[TesterId] AND p.[ClientId] = c.[SupersedesClientId]
                    WHERE c.[RootClientId] IS NULL AND p.[RootClientId] IS NOT NULL;
                    SET @linked = @@ROWCOUNT;
                END");

            // A version whose parent never reached the server: the parent's id is the best root
            // there is, and the parent repairs it if it ever arrives (TestLineage).
            migrationBuilder.Sql(@"
                UPDATE [MachineTests] SET [RootClientId] = [SupersedesClientId]
                WHERE [RootClientId] IS NULL AND [SupersedesClientId] IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TesterId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RootClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaseClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HeadClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomingClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergedClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DetectedOn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OverlappingFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TesterId_MergedFromClientId",
                table: "MachineTests",
                columns: new[] { "TesterId", "MergedFromClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_TesterId_RootClientId",
                table: "MachineTests",
                columns: new[] { "TesterId", "RootClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_TesterId_IncomingClientId",
                table: "SyncConflicts",
                columns: new[] { "TesterId", "IncomingClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_TesterId_RootClientId",
                table: "SyncConflicts",
                columns: new[] { "TesterId", "RootClientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncConflicts");

            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TesterId_MergedFromClientId",
                table: "MachineTests");

            migrationBuilder.DropIndex(
                name: "IX_MachineTests_TesterId_RootClientId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "MergedFromClientId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "RootClientId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "SuccessorStamp",
                table: "MachineTests");
        }
    }
}
