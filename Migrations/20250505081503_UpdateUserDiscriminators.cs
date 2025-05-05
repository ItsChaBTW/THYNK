using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserDiscriminators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update discriminator for regular users
            migrationBuilder.Sql(
                @"UPDATE [AspNetUsers] 
                SET [Discriminator] = 'ApplicationUser' 
                WHERE [Discriminator] IS NULL OR [Discriminator] = ''");

            // Update discriminator for LGU users (UserRoleType.LGU = 1)
            migrationBuilder.Sql(
                @"UPDATE [AspNetUsers] 
                SET [Discriminator] = 'LGUUser' 
                WHERE [UserRole] = 1");

            migrationBuilder.AlterColumn<string>(
                name: "IDDocumentUrl",
                table: "AspNetUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IDDocumentUrl",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
