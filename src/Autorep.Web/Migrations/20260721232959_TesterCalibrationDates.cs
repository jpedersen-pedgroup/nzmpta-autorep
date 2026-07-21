using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autorep.Web.Migrations
{
    /// <inheritdoc />
    public partial class TesterCalibrationDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CalAirFlowMetersExpiry",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CalPulsatorTestersExpiry",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CalVacuumGaugesExpiry",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            // Seed each tester's profile from their most recent test that recorded a value for
            // that instrument — calibration dates used to be captured per-test (new-format
            // payloads at $.calX; migrated legacy payloads at $.farmInfo.DateX). Legacy zero
            // dates (0001-01-01) are placeholders, not data. Only fills NULLs, so re-running
            // environments where testers have already maintained their profile is safe.
            var fields = new (string Column, string NewPath, string LegacyPath)[]
            {
                ("CalAirFlowMetersExpiry", "$.calAirFlowMeters", "$.farmInfo.DateAirFlowMeters"),
                ("CalPulsatorTestersExpiry", "$.calPulsatorTesters", "$.farmInfo.DatePulsatorTesters"),
                ("CalVacuumGaugesExpiry", "$.calVacuumGauges", "$.farmInfo.DateVacuumGauges"),
            };
            foreach (var (column, newPath, legacyPath) in fields)
            {
                migrationBuilder.Sql($@"
UPDATE u SET {column} = src.D
FROM AspNetUsers u
CROSS APPLY (
    SELECT TOP 1 TRY_CONVERT(date, COALESCE(
        JSON_VALUE(t.PayloadJson, '{newPath}'),
        JSON_VALUE(t.PayloadJson, '{legacyPath}'))) AS D
    FROM MachineTests t
    WHERE t.TesterId = u.Id AND ISJSON(t.PayloadJson) = 1
      AND TRY_CONVERT(date, COALESCE(
        JSON_VALUE(t.PayloadJson, '{newPath}'),
        JSON_VALUE(t.PayloadJson, '{legacyPath}'))) > '1900-01-01'
    ORDER BY t.CreatedAt DESC
) src
WHERE u.{column} IS NULL;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalAirFlowMetersExpiry",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalPulsatorTestersExpiry",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalVacuumGaugesExpiry",
                table: "AspNetUsers");
        }
    }
}
