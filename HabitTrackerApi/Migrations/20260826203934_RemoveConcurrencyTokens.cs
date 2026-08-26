using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserBadges");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserBackgroundUnlocks");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "PetAccessoryUnlocks");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "HabitCompletions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Flowers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "BookReadingLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                table: "UserBackgroundUnlocks",
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
                table: "PetAccessoryUnlocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "NotificationPreferences",
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

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Books",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "BookReadingLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
