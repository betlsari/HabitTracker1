using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitCompletionRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId",
                table: "HabitCompletions",
                column: "HabitId");

            migrationBuilder.AddForeignKey(
                name: "FK_HabitCompletions_Habits_HabitId",
                table: "HabitCompletions",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HabitCompletions_Habits_HabitId",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_HabitId",
                table: "HabitCompletions");
        }
    }
}
