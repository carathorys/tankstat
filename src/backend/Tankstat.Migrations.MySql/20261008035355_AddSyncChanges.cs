using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddSyncChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TargetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ExpectedVersion = table.Column<int>(type: "int", nullable: true),
                    Payload = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    ResultId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ResultVersion = table.Column<int>(type: "int", nullable: true),
                    ReasonKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    ReasonArgs = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncChanges_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_OwnerId",
                table: "SyncChanges",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Status_ReceivedAt",
                table: "SyncChanges",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_SubmittedById_Status",
                table: "SyncChanges",
                columns: new[] { "SubmittedById", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_VehicleId_Status",
                table: "SyncChanges",
                columns: new[] { "VehicleId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncChanges");
        }
    }
}
