using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_AspNetUsers_IssuedByUserId",
                table: "Alerts");

            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_DisasterReports_DisasterReportId",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_DisasterReportId",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CenterLatitude",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "CenterLongitude",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "DisasterReportId",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "RadiusKm",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Alerts");

            migrationBuilder.RenameColumn(
                name: "IssuedByUserId",
                table: "Alerts",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Alerts_IssuedByUserId",
                table: "Alerts",
                newName: "IX_Alerts_UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_AspNetUsers_UserId",
                table: "Alerts",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_AspNetUsers_UserId",
                table: "Alerts");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Alerts",
                newName: "IssuedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Alerts_UserId",
                table: "Alerts",
                newName: "IX_Alerts_IssuedByUserId");

            migrationBuilder.AddColumn<double>(
                name: "CenterLatitude",
                table: "Alerts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CenterLongitude",
                table: "Alerts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisasterReportId",
                table: "Alerts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "RadiusKm",
                table: "Alerts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Alerts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_DisasterReportId",
                table: "Alerts",
                column: "DisasterReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_AspNetUsers_IssuedByUserId",
                table: "Alerts",
                column: "IssuedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_DisasterReports_DisasterReportId",
                table: "Alerts",
                column: "DisasterReportId",
                principalTable: "DisasterReports",
                principalColumn: "Id");
        }
    }
}
