using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddPhotoDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoDrafts_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoDrafts_CreatedAt",
                table: "PhotoDrafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoDrafts_OwnerId",
                table: "PhotoDrafts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoDrafts_VehicleId_CreatedById",
                table: "PhotoDrafts",
                columns: new[] { "VehicleId", "CreatedById" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoDrafts");
        }
    }
}
