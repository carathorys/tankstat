using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AllowIncompleteLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CostId",
                table: "Expenses");

            migrationBuilder.AlterColumn<decimal>(
                name: "Volume",
                table: "Refuelings",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,3)",
                oldPrecision: 9,
                oldScale: 3);

            migrationBuilder.AlterColumn<Guid>(
                name: "OdometerReadingId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<int>(
                name: "FilledFromPhoto",
                table: "Refuelings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Refuelings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<int>(
                name: "FilledFromPhoto",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Expenses",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings",
                column: "CostId",
                unique: true,
                filter: "[CostId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings",
                column: "OdometerReadingId",
                unique: true,
                filter: "[OdometerReadingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CostId",
                table: "Expenses",
                column: "CostId",
                unique: true,
                filter: "[CostId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CostId",
                table: "Expenses");

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
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,3)",
                oldPrecision: 9,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OdometerReadingId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Refuelings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CostId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_CostId",
                table: "Refuelings",
                column: "CostId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refuelings_OdometerReadingId",
                table: "Refuelings",
                column: "OdometerReadingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CostId",
                table: "Expenses",
                column: "CostId",
                unique: true);
        }
    }
}
