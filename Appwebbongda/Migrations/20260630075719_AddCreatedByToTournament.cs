using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appwebbongda.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByToTournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Tournaments",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Tournaments");
        }
    }
}
