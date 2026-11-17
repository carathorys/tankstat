using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.Sqlite
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Failure = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CancelledById = table.Column<Guid>(type: "TEXT", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Server = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Plan = table.Column<string>(type: "TEXT", maxLength: 1000000, nullable: false),
                    Cars = table.Column<int>(type: "INTEGER", nullable: false),
                    Entries = table.Column<int>(type: "INTEGER", nullable: false),
                    DoneCars = table.Column<int>(type: "INTEGER", nullable: false),
                    DoneEntries = table.Column<int>(type: "INTEGER", nullable: false),
                    Report = table.Column<string>(type: "TEXT", maxLength: 1000000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

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
