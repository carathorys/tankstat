using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddEntityVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Vehicles",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Refuelings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "RecurringExpenses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Expenses");
        }
    }
}
