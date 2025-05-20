using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddTimestampFieldsToDisasterReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateInProgress",
                table: "DisasterReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateVerified",
                table: "DisasterReports",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateInProgress",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "DateVerified",
                table: "DisasterReports");
        }
    }
}
