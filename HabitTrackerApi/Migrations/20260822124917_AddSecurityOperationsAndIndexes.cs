using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityOperationsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "RefreshTokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "RefreshTokens",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_CreatedAt",
                table: "Pets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HabitCompletions_CompletionDate",
                table: "HabitCompletions",
                column: "CompletionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Books_CreatedAt",
                table: "Books",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BookReadingLogs_ReadDate",
                table: "BookReadingLogs",
                column: "ReadDate");

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEvents_Email_CreatedAt",
                table: "AuthAuditEvents",
                columns: new[] { "Email", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEvents_UserId_CreatedAt",
                table: "AuthAuditEvents",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthAuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_Pets_CreatedAt",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_HabitCompletions_CompletionDate",
                table: "HabitCompletions");

            migrationBuilder.DropIndex(
                name: "IX_Books_CreatedAt",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_BookReadingLogs_ReadDate",
                table: "BookReadingLogs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "RefreshTokens");
        }
    }
}
