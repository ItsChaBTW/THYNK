using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace THYNK.Migrations
{
    /// <inheritdoc />
    public partial class AddEvacuationSitesFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvacuationSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContactNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContactPerson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HasWater = table.Column<bool>(type: "bit", nullable: false),
                    HasElectricity = table.Column<bool>(type: "bit", nullable: false),
                    HasMedicalSupplies = table.Column<bool>(type: "bit", nullable: false),
                    HasInternet = table.Column<bool>(type: "bit", nullable: false),
                    IsWheelchairAccessible = table.Column<bool>(type: "bit", nullable: false),
                    HasBathroomFacilities = table.Column<bool>(type: "bit", nullable: false),
                    HasKitchen = table.Column<bool>(type: "bit", nullable: false),
                    HasSleepingFacilities = table.Column<bool>(type: "bit", nullable: false),
                    ManagedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvacuationSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvacuationSites_AspNetUsers_ManagedByUserId",
                        column: x => x.ManagedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvacuationSites_ManagedByUserId",
                table: "EvacuationSites",
                column: "ManagedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvacuationSites");
        }
    }
}
