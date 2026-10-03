using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddPhotoReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Locale = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    LastOdometer = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true),
                    Today = table.Column<DateOnly>(type: "date", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModelVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Values = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoReadings_Images_Id",
                        column: x => x.Id,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoReadings_Status_DueAt",
                table: "PhotoReadings",
                columns: new[] { "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoReadings");
        }
    }
}
