using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddLogPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogPhotos_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogPhotos_ImageId",
                table: "LogPhotos",
                column: "ImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogPhotos_LogType_LogId",
                table: "LogPhotos",
                columns: new[] { "LogType", "LogId" });

            migrationBuilder.CreateIndex(
                name: "IX_LogPhotos_OwnerId",
                table: "LogPhotos",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_LogPhotos_VehicleId",
                table: "LogPhotos",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogPhotos");
        }
    }
}
