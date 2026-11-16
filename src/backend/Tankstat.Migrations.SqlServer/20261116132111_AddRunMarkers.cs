using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddRunMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunMarkers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    End = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CancelRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelRequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunMarkers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunMarkerVehicles",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunMarkerVehicles", x => new { x.RunId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_RunMarkerVehicles_RunMarkers_RunId",
                        column: x => x.RunId,
                        principalTable: "RunMarkers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunMarkers_EndedAt",
                table: "RunMarkers",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RunMarkerVehicles_VehicleId",
                table: "RunMarkerVehicles",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunMarkerVehicles");

            migrationBuilder.DropTable(
                name: "RunMarkers");
        }
    }
}
