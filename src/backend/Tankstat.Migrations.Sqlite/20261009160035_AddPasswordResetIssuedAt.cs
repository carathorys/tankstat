using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddPasswordResetIssuedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "PasswordResetTokens",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_IssuedAt",
                table: "PasswordResetTokens",
                column: "IssuedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_IssuedAt",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "PasswordResetTokens");
        }
    }
}
