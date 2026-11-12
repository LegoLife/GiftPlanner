using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiftPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Kids",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // everything from before accounts belongs to the owner (account 1)

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Groups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // everything from before accounts belongs to the owner (account 1)

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Gifts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // everything from before accounts belongs to the owner (account 1)

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    GoogleSubject = table.Column<string>(type: "TEXT", nullable: true),
                    EmailVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            // The owner of the pre-accounts data. On startup, GiftPlanner:OwnerEmail and the old
            // GiftPlanner:PasswordHash are given to it (AccountService.ClaimOwnerAccountAsync).
            migrationBuilder.Sql(
                "INSERT INTO \"Accounts\" (\"Id\", \"DisplayName\", \"EmailVerified\", \"CreatedAt\") " +
                "VALUES (1, 'Owner', 0, '2026-11-12 00:00:00');");

            migrationBuilder.CreateIndex(
                name: "IX_Kids_AccountId",
                table: "Kids",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_AccountId",
                table: "Groups",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Gifts_AccountId",
                table: "Gifts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GoogleSubject",
                table: "Accounts",
                column: "GoogleSubject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedEmail",
                table: "Accounts",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Gifts_Accounts_AccountId",
                table: "Gifts",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Accounts_AccountId",
                table: "Groups",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Kids_Accounts_AccountId",
                table: "Kids",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gifts_Accounts_AccountId",
                table: "Gifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Accounts_AccountId",
                table: "Groups");

            migrationBuilder.DropForeignKey(
                name: "FK_Kids_Accounts_AccountId",
                table: "Kids");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Kids_AccountId",
                table: "Kids");

            migrationBuilder.DropIndex(
                name: "IX_Groups_AccountId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Gifts_AccountId",
                table: "Gifts");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Kids");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Gifts");
        }
    }
}
