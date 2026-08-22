using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimisticConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_HabitId",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_BookReadingLogs_BookId",
                table: "BookReadingLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "UserNotifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "UserBadges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "RefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Pets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Habits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "HabitCompletions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsOnTime",
                table: "HabitCompletions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PetStreakBonusXp",
                table: "HabitCompletions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Flowers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "DeviceTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "CompletionBonusAwarded",
                table: "Books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Books",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyCompleted",
                table: "Books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "Books",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "BookReadingLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId_CompletionDate",
                table: "HabitCompletions",
                columns: new[] { "HabitId", "CompletionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BookReadingLogs_BookId_ReadDate",
                table: "BookReadingLogs",
                columns: new[] { "BookId", "ReadDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_HabitId_CompletionDate",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_BookReadingLogs_BookId_ReadDate",
                table: "BookReadingLogs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserBadges");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "IsOnTime",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "PetStreakBonusXp",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Flowers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "CompletionBonusAwarded",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ManuallyCompleted",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "BookReadingLogs");

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_HabitId",
                table: "HabitCompletions",
                column: "HabitId");

            migrationBuilder.CreateIndex(
                name: "IX_BookReadingLogs_BookId",
                table: "BookReadingLogs",
                column: "BookId");
        }
    }
}
