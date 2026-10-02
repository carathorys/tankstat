using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RecipientId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Topic = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SubjectType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ContextType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ContextId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Occurrence = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Args = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    Before = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_ReadAt_UpdatedAt",
                table: "Notifications",
                columns: new[] { "RecipientId", "ReadAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_Topic_SubjectType_SubjectId_Contex~",
                table: "Notifications",
                columns: new[] { "RecipientId", "Topic", "SubjectType", "SubjectId", "ContextId", "Occurrence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
