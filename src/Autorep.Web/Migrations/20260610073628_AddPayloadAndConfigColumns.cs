using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadAndConfigColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "MachineTests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtmosPressureSeaLevel",
                table: "MachineConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackLiner",
                table: "MachineConfigurations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlantSize",
                table: "MachineConfigurations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PulsatorBrand",
                table: "MachineConfigurations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PulsatorConfiguration",
                table: "MachineConfigurations",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "AtmosPressureSeaLevel",
                table: "MachineConfigurations");

            migrationBuilder.DropColumn(
                name: "BackLiner",
                table: "MachineConfigurations");

            migrationBuilder.DropColumn(
                name: "PlantSize",
                table: "MachineConfigurations");

            migrationBuilder.DropColumn(
                name: "PulsatorBrand",
                table: "MachineConfigurations");

            migrationBuilder.DropColumn(
                name: "PulsatorConfiguration",
                table: "MachineConfigurations");
        }
    }
}
