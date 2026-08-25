using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class ActiveRecordUniquenessAndReadingLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits");

            migrationBuilder.DropIndex(
                name: "IX_Books_UserId_NormalizedTitle",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Habits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true,
                filter: "\"IsArchived\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Books_UserId_NormalizedTitle",
                table: "Books",
                columns: new[] { "UserId", "NormalizedTitle" },
                unique: true,
                filter: "\"IsArchived\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits");

            migrationBuilder.DropIndex(
                name: "IX_Books_UserId_NormalizedTitle",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Habits",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_UserId_NormalizedTitle",
                table: "Books",
                columns: new[] { "UserId", "NormalizedTitle" },
                unique: true);
        }
    }
}
