using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class ReintroduceClientRequestIdForCompletionsAndLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "HabitCompletions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "BookReadingLogs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId_ClientRequestId",
                table: "HabitCompletions",
                columns: new[] { "HabitId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookReadingLogs_BookId_ClientRequestId",
                table: "BookReadingLogs",
                columns: new[] { "BookId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_HabitId_ClientRequestId",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_BookReadingLogs_BookId_ClientRequestId",
                table: "BookReadingLogs");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "BookReadingLogs");
        }
    }
}
