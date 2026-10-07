using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class MachineTestSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "MachineTests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedById",
                table: "MachineTests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedReason",
                table: "MachineTests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MachineTests",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "DeletedReason",
                table: "MachineTests");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MachineTests");
        }
    }
}
