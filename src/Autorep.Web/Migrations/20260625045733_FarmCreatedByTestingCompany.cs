using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class FarmCreatedByTestingCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByTestingCompanyId",
                table: "Farms",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByTestingCompanyId",
                table: "Farms");
        }
    }
}
