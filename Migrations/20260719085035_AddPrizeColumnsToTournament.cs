using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appwebbongda.Migrations
{
    /// <inheritdoc />
    public partial class AddPrizeColumnsToTournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanExpiry",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TournamentsCreated",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AdminFee",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BankAccount",
                table: "Tournaments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankHolder",
                table: "Tournaments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Tournaments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChatEnabled",
                table: "Tournaments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EntryFee",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Tournaments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Prize1",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Prize2",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Prize3",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingSum",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Season",
                table: "Tournaments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasPaid",
                table: "Registrations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Registrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayPenalty",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomePenalty",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsThirdPlace",
                table: "Matches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "TournamentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TournamentId",
                table: "ChatMessages",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId",
                table: "ChatMessages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PlanExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TournamentsCreated",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdminFee",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "BankAccount",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "BankHolder",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ChatEnabled",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "EntryFee",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Prize1",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Prize2",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Prize3",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "RatingSum",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "HasPaid",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "AwayPenalty",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomePenalty",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsThirdPlace",
                table: "Matches");
        }
    }
}
