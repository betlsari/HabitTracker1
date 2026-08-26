using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveQuietHoursAndDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigestEnabled",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "DigestHourUtc",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "QuietHoursEnd",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "QuietHoursStart",
                table: "NotificationPreferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigestEnabled",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DigestHourUtc",
                table: "NotificationPreferences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursEnd",
                table: "NotificationPreferences",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursStart",
                table: "NotificationPreferences",
                type: "time without time zone",
                nullable: true);
        }
    }
}
