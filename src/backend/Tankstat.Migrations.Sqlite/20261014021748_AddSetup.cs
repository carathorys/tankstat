using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SetupCompletedAt",
                table: "InstanceState",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupCompletedVia",
                table: "InstanceState",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupPendingSince",
                table: "InstanceState",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupCompletedAt",
                table: "InstanceState");

            migrationBuilder.DropColumn(
                name: "SetupCompletedVia",
                table: "InstanceState");

            migrationBuilder.DropColumn(
                name: "SetupPendingSince",
                table: "InstanceState");
        }
    }
}
