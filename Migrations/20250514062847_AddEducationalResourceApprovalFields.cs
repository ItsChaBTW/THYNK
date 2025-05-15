using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationalResourceApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "EducationalResources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedDate",
                table: "EducationalResources",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "EducationalResources",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "EducationalResources",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationalResources_CreatedById",
                table: "EducationalResources",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_EducationalResources_AspNetUsers_CreatedById",
                table: "EducationalResources",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EducationalResources_AspNetUsers_CreatedById",
                table: "EducationalResources");

            migrationBuilder.DropIndex(
                name: "IX_EducationalResources_CreatedById",
                table: "EducationalResources");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "EducationalResources");

            migrationBuilder.DropColumn(
                name: "ApprovedDate",
                table: "EducationalResources");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "EducationalResources");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "EducationalResources");
        }
    }
}
