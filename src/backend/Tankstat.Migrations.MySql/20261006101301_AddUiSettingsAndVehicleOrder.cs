using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
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
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GridId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Order = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    Hidden = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    PageSize = table.Column<int>(type: "int", nullable: false),
                    SortColumn = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    SortDirection = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridSettings", x => new { x.UserId, x.GridId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UiSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NavOpen = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Language = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UiSettings", x => x.UserId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VehicleOrders",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
