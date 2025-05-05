using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAlertUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_AspNetUsers_IssuedByUserId",
                table: "Alerts",
                column: "IssuedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_AspNetUsers_IssuedByUserId",
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
    }
}
