using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClientRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pets_UserId_ClientRequestId",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId_ClientRequestId",
                table: "Habits");

            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_HabitId_ClientRequestId",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_Books_UserId_ClientRequestId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_BookReadingLogs_BookId_ClientRequestId",
                table: "BookReadingLogs");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "BookReadingLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Pets_UserId",
                table: "Pets",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pets_UserId",
                table: "Pets");

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "Pets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "Habits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "HabitCompletions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "Books",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "BookReadingLogs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pets_UserId_ClientRequestId",
                table: "Pets",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId_ClientRequestId",
                table: "Habits",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId_ClientRequestId",
                table: "HabitCompletions",
                columns: new[] { "HabitId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Books_UserId_ClientRequestId",
                table: "Books",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookReadingLogs_BookId_ClientRequestId",
                table: "BookReadingLogs",
                columns: new[] { "BookId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }
    }
}
