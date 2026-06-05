using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class FarmDetailsAndReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MachineTests_Farms_FarmId",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Farms");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Farms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Farms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Farms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Farms",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Farms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FarmerName",
                table: "Farms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Farms",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MilkSupplyCompanyId",
                table: "Farms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Farms",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostCode",
                table: "Farms",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RapidNumber",
                table: "Farms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "Farms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyNumber",
                table: "Farms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Town",
                table: "Farms",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Farms",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MilkSupplyCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilkSupplyCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Farms_IsActive",
                table: "Farms",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_MilkSupplyCompanyId",
                table: "Farms",
                column: "MilkSupplyCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_Name",
                table: "Farms",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_RegionId",
                table: "Farms",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_SupplyNumber",
                table: "Farms",
                column: "SupplyNumber");

            migrationBuilder.CreateIndex(
                name: "IX_MilkSupplyCompanies_Name",
                table: "MilkSupplyCompanies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Farms_MilkSupplyCompanies_MilkSupplyCompanyId",
                table: "Farms",
                column: "MilkSupplyCompanyId",
                principalTable: "MilkSupplyCompanies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Farms_Regions_RegionId",
                table: "Farms",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MachineTests_Farms_FarmId",
                table: "MachineTests",
                column: "FarmId",
                principalTable: "Farms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Farms_MilkSupplyCompanies_MilkSupplyCompanyId",
                table: "Farms");

            migrationBuilder.DropForeignKey(
                name: "FK_Farms_Regions_RegionId",
                table: "Farms");

            migrationBuilder.DropForeignKey(
                name: "FK_MachineTests_Farms_FarmId",
                table: "MachineTests");

            migrationBuilder.DropTable(
                name: "MilkSupplyCompanies");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Farms_IsActive",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_MilkSupplyCompanyId",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_Name",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_RegionId",
                table: "Farms");

            migrationBuilder.DropIndex(
                name: "IX_Farms_SupplyNumber",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "FarmerName",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "MilkSupplyCompanyId",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "PostCode",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "RapidNumber",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "SupplyNumber",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "Town",
                table: "Farms");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Farms");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Farms",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Farms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MachineTests_Farms_FarmId",
                table: "MachineTests",
                column: "FarmId",
                principalTable: "Farms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
