using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tankstat.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddImageFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Folder",
                table: "Images",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_Folder",
                table: "Images",
                column: "Folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Images_Folder",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "Folder",
                table: "Images");
        }
    }
}
