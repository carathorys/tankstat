using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
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
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupCompletedVia",
                table: "InstanceState",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupPendingSince",
                table: "InstanceState",
                type: "datetime2",
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
