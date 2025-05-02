using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailedLocationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "DisasterReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "DisasterReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "DisasterReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Purok",
                table: "DisasterReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "City",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "Purok",
                table: "DisasterReports");
        }
    }
}
