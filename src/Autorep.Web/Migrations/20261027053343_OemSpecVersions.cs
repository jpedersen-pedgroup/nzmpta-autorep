using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <summary>
    /// OEM specs become dated versions (EquipmentSpecVersions). Each spec held in
    /// EquipmentItems.SpecJson moves across, undated, as the version in effect from the start - what
    /// every test signed off so far was judged against - before the column goes.
    /// </summary>
    public partial class OemSpecVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentSpecVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    SpecJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentSpecVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentSpecVersions_EquipmentItems_EquipmentItemId",
                        column: x => x.EquipmentItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentSpecVersions_EquipmentItemId_EffectiveFrom",
                table: "EquipmentSpecVersions",
                columns: new[] { "EquipmentItemId", "EffectiveFrom" },
                unique: true,
                filter: "[EffectiveFrom] IS NOT NULL");

            migrationBuilder.Sql(
                """
                INSERT INTO [EquipmentSpecVersions] ([Id], [EquipmentItemId], [EffectiveFrom], [SpecJson], [CreatedAt])
                SELECT NEWID(), [Id], NULL, [SpecJson], SYSDATETIMEOFFSET()
                FROM [EquipmentItems]
                WHERE [SpecJson] IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SpecJson",
                table: "EquipmentItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecJson",
                table: "EquipmentItems",
                type: "nvarchar(max)",
                nullable: true);

            // Back to one spec per item: the latest version, dated or not.
            migrationBuilder.Sql(
                """
                UPDATE [i] SET [SpecJson] = [v].[SpecJson]
                FROM [EquipmentItems] AS [i]
                CROSS APPLY (
                    SELECT TOP 1 [SpecJson] FROM [EquipmentSpecVersions]
                    WHERE [EquipmentItemId] = [i].[Id]
                    ORDER BY ISNULL([EffectiveFrom], '0001-01-01') DESC
                ) AS [v];
                """);

            migrationBuilder.DropTable(
                name: "EquipmentSpecVersions");
        }
    }
}
