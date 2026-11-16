using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTyreSetReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ReminderDismissedFor",
                table: "TyreSets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderMonth",
                table: "TyreSets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReminderSince",
                table: "TyreSets",
                type: "date",
                nullable: true);

            // The sets that exist already count from the day of the upgrade (#292, the owner's decision 78): a season that began before
            // this month does not remind, so nothing is due the moment the column arrives for a set stored since last year.
            migrationBuilder.Sql("""UPDATE "TyreSets" SET "ReminderSince" = (now() AT TIME ZONE 'utc')::date;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderDismissedFor",
                table: "TyreSets");

            migrationBuilder.DropColumn(
                name: "ReminderMonth",
                table: "TyreSets");

            migrationBuilder.DropColumn(
                name: "ReminderSince",
                table: "TyreSets");
        }
    }
}
