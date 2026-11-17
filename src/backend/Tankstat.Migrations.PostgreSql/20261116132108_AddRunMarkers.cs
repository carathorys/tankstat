using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedById = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    End = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CancelRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelRequestedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunMarkers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunMarkerVehicles",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false)
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
