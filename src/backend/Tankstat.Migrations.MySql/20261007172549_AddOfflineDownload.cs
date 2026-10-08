using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddOfflineDownload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Vehicles",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Refuelings",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RecurringExpenses",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Expenses",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateTable(
                name: "OfflineSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefaultWindow = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineSettings", x => x.UserId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OfflineVehicleSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Window = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineVehicleSettings", x => new { x.UserId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_OfflineVehicleSettings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    EntityId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tombstones", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_VehicleId_UpdatedAt",
                table: "Refuelings",
                columns: new[] { "VehicleId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_VehicleId_UpdatedAt",
                table: "RecurringExpenses",
                columns: new[] { "VehicleId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_VehicleId_UpdatedAt",
                table: "Expenses",
                columns: new[] { "VehicleId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineVehicleSettings_VehicleId",
                table: "OfflineVehicleSettings",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_PurgedAt",
                table: "Tombstones",
                column: "PurgedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_VehicleId_PurgedAt",
                table: "Tombstones",
                columns: new[] { "VehicleId", "PurgedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfflineSettings");

            migrationBuilder.DropTable(
                name: "OfflineVehicleSettings");

            migrationBuilder.DropTable(
                name: "Tombstones");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_VehicleId_UpdatedAt",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_RecurringExpenses_VehicleId_UpdatedAt",
                table: "RecurringExpenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_VehicleId_UpdatedAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Expenses");
        }
    }
}
