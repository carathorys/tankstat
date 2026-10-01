using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddLogsSharingUnitsAndCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.RenameColumn(
                name: "Liters",
                table: "Refuelings",
                newName: "Volume");

            migrationBuilder.AddColumn<string>(
                name: "OdometerUnit",
                table: "Vehicles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Kilometers");

            migrationBuilder.AddColumn<string>(
                name: "VolumeUnit",
                table: "Vehicles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Liters");

            migrationBuilder.AddColumn<Guid>(
                name: "CostId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Refuelings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Refuelings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OdometerReadingId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Costs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Costs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Costs_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OdometerReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdometerReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OdometerReadings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GranteeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Feature = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceGrants_Users_GranteeId",
                        column: x => x.GranteeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing logs keep their data: each gets an odometer reading and a cost that reuse the log's id
            // (the old OdometerKm / TotalCost columns are dropped at the end). Costs recorded before currencies existed are taken as EUR.
            migrationBuilder.Sql(
                "INSERT INTO [OdometerReadings] ([Id], [OwnerId], [VehicleId], [Date], [Value], [DeletedAt]) " +
                "SELECT [Id], [OwnerId], [VehicleId], [Date], [OdometerKm], NULL FROM [Refuelings]");
            migrationBuilder.Sql(
                "INSERT INTO [Costs] ([Id], [OwnerId], [VehicleId], [Date], [Amount], [Currency], [DeletedAt]) " +
                "SELECT [Id], [OwnerId], [VehicleId], [Date], [TotalCost], 'EUR', NULL FROM [Refuelings]");
            migrationBuilder.Sql(
                "UPDATE [Refuelings] SET [OdometerReadingId] = [Id], [CostId] = [Id]");

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings",
                column: "CostId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_DeletedAt",
                table: "Refuelings",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings",
                column: "OdometerReadingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Costs_OwnerId",
                table: "Costs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_VehicleId_Date",
                table: "Costs",
                columns: new[] { "VehicleId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_OdometerReadings_OwnerId",
                table: "OdometerReadings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerReadings_VehicleId_Date",
                table: "OdometerReadings",
                columns: new[] { "VehicleId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGrants_GranteeId_ResourceType_Feature",
                table: "ResourceGrants",
                columns: new[] { "GranteeId", "ResourceType", "Feature" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGrants_ResourceType_ResourceId_GranteeId_Feature",
                table: "ResourceGrants",
                columns: new[] { "ResourceType", "ResourceId", "GranteeId", "Feature" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Refuelings_Costs_CostId",
                table: "Refuelings",
                column: "CostId",
                principalTable: "Costs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refuelings_OdometerReadings_OdometerReadingId",
                table: "Refuelings",
                column: "OdometerReadingId",
                principalTable: "OdometerReadings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "OdometerKm",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "Refuelings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Refuelings_Costs_CostId",
                table: "Refuelings");

            migrationBuilder.DropForeignKey(
                name: "FK_Refuelings_OdometerReadings_OdometerReadingId",
                table: "Refuelings");

            migrationBuilder.DropTable(
                name: "Costs");

            migrationBuilder.DropTable(
                name: "OdometerReadings");

            migrationBuilder.DropTable(
                name: "ResourceGrants");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_DeletedAt",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "OdometerUnit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VolumeUnit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CostId",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "OdometerReadingId",
                table: "Refuelings");

            migrationBuilder.RenameColumn(
                name: "Volume",
                table: "Refuelings",
                newName: "Liters");

            migrationBuilder.AddColumn<int>(
                name: "OdometerKm",
                table: "Refuelings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "Refuelings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
