using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
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
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LogType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    LogId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ImageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
