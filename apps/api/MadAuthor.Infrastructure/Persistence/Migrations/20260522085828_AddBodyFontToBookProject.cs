using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadAuthor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBodyFontToBookProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyFont",
                table: "BookProjects",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyFont",
                table: "BookProjects");
        }
    }
}
