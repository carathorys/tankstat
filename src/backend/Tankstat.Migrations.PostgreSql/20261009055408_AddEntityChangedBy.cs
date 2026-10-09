using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddEntityChangedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAt",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedById",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastChange",
                table: "Vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAt",
                table: "Refuelings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedById",
                table: "Refuelings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastChange",
                table: "Refuelings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAt",
                table: "RecurringExpenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedById",
                table: "RecurringExpenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastChange",
                table: "RecurringExpenses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAt",
                table: "Expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedById",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastChange",
                table: "Expenses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangedAt",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ChangedById",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastChange",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ChangedAt",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "ChangedById",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "LastChange",
                table: "Refuelings");

            migrationBuilder.DropColumn(
                name: "ChangedAt",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "ChangedById",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "LastChange",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "ChangedAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ChangedById",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LastChange",
                table: "Expenses");
        }
    }
}
