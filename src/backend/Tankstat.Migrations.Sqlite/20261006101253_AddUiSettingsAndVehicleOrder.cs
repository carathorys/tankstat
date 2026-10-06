using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddUiSettingsAndVehicleOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GridSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GridId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Order = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Hidden = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    PageSize = table.Column<int>(type: "INTEGER", nullable: false),
                    SortColumn = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortDirection = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridSettings", x => new { x.UserId, x.GridId });
                });

            migrationBuilder.CreateTable(
                name: "UiSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NavOpen = table.Column<bool>(type: "INTEGER", nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UiSettings", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "VehicleOrders",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehicleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleOrders", x => new { x.UserId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_VehicleOrders_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleOrders_VehicleId",
                table: "VehicleOrders",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GridSettings");

            migrationBuilder.DropTable(
                name: "UiSettings");

            migrationBuilder.DropTable(
                name: "VehicleOrders");
        }
    }
}
