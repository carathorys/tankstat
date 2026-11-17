using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StartedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Failure = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    CancelledById = table.Column<Guid>(type: "char(36)", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Server = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Plan = table.Column<string>(type: "longtext", maxLength: 1000000, nullable: false),
                    Cars = table.Column<int>(type: "int", nullable: false),
                    Entries = table.Column<int>(type: "int", nullable: false),
                    DoneCars = table.Column<int>(type: "int", nullable: false),
                    DoneEntries = table.Column<int>(type: "int", nullable: false),
                    Report = table.Column<string>(type: "longtext", maxLength: 1000000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_StartedById",
                table: "ImportRuns",
                column: "StartedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_State_QueuedAt",
                table: "ImportRuns",
                columns: new[] { "State", "QueuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportRuns");
        }
    }
}
