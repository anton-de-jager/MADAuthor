using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadAuthor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributionJsonToBookAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributionJson",
                table: "BookAssets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttributionJson",
                table: "BookAssets");
        }
    }
}
