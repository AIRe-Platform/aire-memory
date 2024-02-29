using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aire.Memory.Migrations
{
    /// <inheritdoc />
    public partial class RemoveQuestionnairePreliminaryField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preliminary",
                table: "Questionnaires");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preliminary",
                table: "Questionnaires",
                type: "text",
                nullable: true);
        }
    }
}
