using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRecalculationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecalculationOutboxItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecalculationOutboxItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HabitId = table.Column<int>(type: "integer", nullable: true),
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecalculationOutboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecalculationOutboxItems_Status_NextAttemptAt_CreatedAt",
                table: "RecalculationOutboxItems",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });
        }
    }
}
