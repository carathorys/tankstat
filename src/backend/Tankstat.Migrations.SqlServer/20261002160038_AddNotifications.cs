using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Occurrence = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Args = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_ReadAt_UpdatedAt",
                table: "Notifications",
                columns: new[] { "RecipientId", "ReadAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_Topic_SubjectType_SubjectId_ContextId_Occurrence",
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
