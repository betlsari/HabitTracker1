using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedNameColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId",
                table: "Habits");

            migrationBuilder.AddColumn<string>(
                name: "EquippedAccessory",
                table: "Pets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Habits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EquippedBackground",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PetAccessoryUnlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PetId = table.Column<int>(type: "integer", nullable: false),
                    Accessory = table.Column<string>(type: "text", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PetAccessoryUnlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PetAccessoryUnlocks_Pets_PetId",
                        column: x => x.PetId,
                        principalTable: "Pets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBackgroundUnlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Background = table.Column<string>(type: "text", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBackgroundUnlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBackgroundUnlocks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
                migrationBuilder.Sql(@"
    DELETE FROM ""Habits"" h
    USING (
        SELECT ""Id"",
               ROW_NUMBER() OVER (PARTITION BY ""UserId"", UPPER(""Name"") ORDER BY ""Id"") AS rn
        FROM ""Habits""
    ) ranked
    WHERE h.""Id"" = ranked.""Id"" AND ranked.rn > 1;
");

migrationBuilder.Sql(@"UPDATE ""Habits"" SET ""NormalizedName"" = UPPER(""Name"");");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PetAccessoryUnlocks_PetId_Accessory",
                table: "PetAccessoryUnlocks",
                columns: new[] { "PetId", "Accessory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBackgroundUnlocks_UserId_Background",
                table: "UserBackgroundUnlocks",
                columns: new[] { "UserId", "Background" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PetAccessoryUnlocks");

            migrationBuilder.DropTable(
                name: "UserBackgroundUnlocks");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId_NormalizedName",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "EquippedAccessory",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "EquippedBackground",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId",
                table: "Habits",
                column: "UserId");
        }
    }
}
