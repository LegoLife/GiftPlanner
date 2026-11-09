using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiftPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Gifts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Gifts");
        }
    }
}
