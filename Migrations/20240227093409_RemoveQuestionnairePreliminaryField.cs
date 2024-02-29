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

            migrationBuilder.DropColumn(
                name: "encrypted_chat_state",
                table: "ChatLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preliminary",
                table: "Questionnaires",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "encrypted_chat_state",
                table: "ChatLogs",
                type: "text",
                nullable: true);
        }
    }
}
