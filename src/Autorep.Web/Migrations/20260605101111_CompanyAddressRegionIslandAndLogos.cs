using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class CompanyAddressRegionIslandAndLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "TestingCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "TestingCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "TestingCompanies",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "TestingCompanies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostCode",
                table: "TestingCompanies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Town",
                table: "TestingCompanies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Island",
                table: "Regions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "MilkSupplyCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "MilkSupplyCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "MilkSupplyCompanies",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "MilkSupplyCompanies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoData",
                table: "MilkSupplyCompanies",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "MilkSupplyCompanies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostCode",
                table: "MilkSupplyCompanies",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Town",
                table: "MilkSupplyCompanies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "PostCode",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "Town",
                table: "TestingCompanies");

            migrationBuilder.DropColumn(
                name: "Island",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "LogoContentType",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "LogoData",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "PostCode",
                table: "MilkSupplyCompanies");

            migrationBuilder.DropColumn(
                name: "Town",
                table: "MilkSupplyCompanies");
        }
    }
}
