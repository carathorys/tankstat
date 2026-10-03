using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AllowIncompleteLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Volume",
                table: "Refuelings",
                type: "TEXT",
                precision: 9,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 9,
                oldScale: 3);

            migrationBuilder.AlterColumn<Guid>(
                name: "OdometerReadingId",
                table: "Refuelings",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Refuelings",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "FilledFromPhoto",
                table: "Refuelings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Refuelings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Expenses",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "FilledFromPhoto",
                table: "Expenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Expenses",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilledFromPhoto",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "FilledFromPhoto",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "Expenses");

            migrationBuilder.AlterColumn<decimal>(
                name: "Volume",
                table: "Refuelings",
                type: "TEXT",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 9,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OdometerReadingId",
                table: "Refuelings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Refuelings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Expenses",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
