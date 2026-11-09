using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiftPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiKidMultiGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {










            migrationBuilder.CreateTable(
                name: "GiftGroup",
                columns: table => new
                {
                    GiftsId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftGroup", x => new { x.GiftsId, x.GroupsId });
                    table.ForeignKey(
                        name: "FK_GiftGroup_Gifts_GiftsId",
                        column: x => x.GiftsId,
                        principalTable: "Gifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GiftGroup_Groups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateTable(
                name: "GiftKid",
                columns: table => new
                {
                    GiftsId = table.Column<int>(type: "INTEGER", nullable: false),
                    KidsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftKid", x => new { x.GiftsId, x.KidsId });
                    table.ForeignKey(
                        name: "FK_GiftKid_Gifts_GiftsId",
                        column: x => x.GiftsId,
                        principalTable: "Gifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GiftKid_Kids_KidsId",
                        column: x => x.KidsId,
                        principalTable: "Kids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_GiftGroup_GroupsId",
                table: "GiftGroup",
                column: "GroupsId");
            migrationBuilder.CreateIndex(
                name: "IX_GiftKid_KidsId",
                table: "GiftKid",
                column: "KidsId");
            // Carry existing gifts over: their one kid and one group become the first entries in the new lists.
            migrationBuilder.Sql("INSERT INTO GiftKid (GiftsId, KidsId) SELECT Id, KidId FROM Gifts;");
            migrationBuilder.Sql("INSERT INTO GiftGroup (GiftsId, GroupsId) SELECT Id, GroupId FROM Gifts WHERE GroupId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Gifts_Groups_GroupId",
                table: "Gifts");
            migrationBuilder.DropForeignKey(
                name: "FK_Gifts_Kids_KidId",
                table: "Gifts");
            migrationBuilder.DropIndex(
                name: "IX_Gifts_KidId",
                table: "Gifts");
            migrationBuilder.DropColumn(
                name: "KidId",
                table: "Gifts");
            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "Gifts",
                newName: "PurchasedByGroupId");
            migrationBuilder.RenameIndex(
                name: "IX_Gifts_GroupId",
                table: "Gifts",
                newName: "IX_Gifts_PurchasedByGroupId");
            // The old GroupId column becomes "bought by": only true for gifts that were actually bought.
            migrationBuilder.Sql("UPDATE Gifts SET PurchasedByGroupId = NULL WHERE PurchasedAt IS NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_Gifts_Groups_PurchasedByGroupId",
                table: "Gifts",
                column: "PurchasedByGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gifts_Groups_PurchasedByGroupId",
                table: "Gifts");

            migrationBuilder.DropTable(
                name: "GiftGroup");

            migrationBuilder.DropTable(
                name: "GiftKid");

            migrationBuilder.RenameColumn(
                name: "PurchasedByGroupId",
                table: "Gifts",
                newName: "GroupId");

            migrationBuilder.RenameIndex(
                name: "IX_Gifts_PurchasedByGroupId",
                table: "Gifts",
                newName: "IX_Gifts_GroupId");

            migrationBuilder.AddColumn<int>(
                name: "KidId",
                table: "Gifts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Gifts_KidId",
                table: "Gifts",
                column: "KidId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gifts_Groups_GroupId",
                table: "Gifts",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Gifts_Kids_KidId",
                table: "Gifts",
                column: "KidId",
                principalTable: "Kids",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
