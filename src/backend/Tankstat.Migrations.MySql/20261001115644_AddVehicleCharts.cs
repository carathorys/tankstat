using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddVehicleCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleCharts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Metric = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Grouping = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Range = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Stacked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RangeFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RangeTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsShared = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCharts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleCharts_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCharts_VehicleId_CreatedById",
                table: "VehicleCharts",
                columns: new[] { "VehicleId", "CreatedById" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleCharts");
        }
    }
}
