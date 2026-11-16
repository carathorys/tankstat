using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedById = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Failure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CancelledById = table.Column<Guid>(type: "uuid", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Server = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Plan = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                    Cars = table.Column<int>(type: "integer", nullable: false),
                    Entries = table.Column<int>(type: "integer", nullable: false),
                    DoneCars = table.Column<int>(type: "integer", nullable: false),
                    DoneEntries = table.Column<int>(type: "integer", nullable: false),
                    Report = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: true)
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
