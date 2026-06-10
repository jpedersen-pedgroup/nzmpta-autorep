using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class WizardMachineConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineTestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ClusterCount = table.Column<int>(type: "int", nullable: false),
                    HerdSize = table.Column<int>(type: "int", nullable: true),
                    LastBmcc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MilklineSize = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FlushingPulsationSystem = table.Column<bool>(type: "bit", nullable: false),
                    PulsatorModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    PulsatorCount = table.Column<int>(type: "int", nullable: false),
                    ClawModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ShellModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LinerModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LinerVented = table.Column<bool>(type: "bit", nullable: false),
                    NumberOfVacuumPumps = table.Column<int>(type: "int", nullable: false),
                    PumpLubrication = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VsdFitted = table.Column<bool>(type: "bit", nullable: false),
                    IsoPortsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    HasPulsatorStopSystem = table.Column<bool>(type: "bit", nullable: false),
                    HasAcr = table.Column<bool>(type: "bit", nullable: false),
                    HasBailGates = table.Column<bool>(type: "bit", nullable: false),
                    HasMilkMeters = table.Column<bool>(type: "bit", nullable: false),
                    HasTeatSprayer = table.Column<bool>(type: "bit", nullable: false),
                    HasBackingGate = table.Column<bool>(type: "bit", nullable: false),
                    HasReleaserPump = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineConfigurations_MachineTests_MachineTestId",
                        column: x => x.MachineTestId,
                        principalTable: "MachineTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineConfigurations_MachineTestId",
                table: "MachineConfigurations",
                column: "MachineTestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineConfigurations");
        }
    }
}
