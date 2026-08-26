using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class RemovePushOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushOutboxItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushOutboxItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushOutboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushOutboxItems_Status_NextAttemptAt_CreatedAt",
                table: "PushOutboxItems",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });
        }
    }
}
