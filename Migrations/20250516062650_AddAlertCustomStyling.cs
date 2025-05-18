using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertCustomStyling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundStyle",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ColorScheme",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IconStyle",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundStyle",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ColorScheme",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "IconStyle",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Alerts");
        }
    }
}
