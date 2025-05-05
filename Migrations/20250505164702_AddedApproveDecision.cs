using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddedApproveDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "DisasterReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToId",
                table: "DisasterReports",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "AspNetUsers",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisasterReports_AssignedToId",
                table: "DisasterReports",
                column: "AssignedToId");

            migrationBuilder.AddForeignKey(
                name: "FK_DisasterReports_AspNetUsers_AssignedToId",
                table: "DisasterReports",
                column: "AssignedToId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisasterReports_AspNetUsers_AssignedToId",
                table: "DisasterReports");

            migrationBuilder.DropIndex(
                name: "IX_DisasterReports_AssignedToId",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "AssignedToId",
                table: "DisasterReports");

            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "AspNetUsers");
        }
    }
}
