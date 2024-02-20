using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aire.Memory.Migrations
{
    /// <inheritdoc />
    public partial class AddChatState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "encrypted_chat_state",
                table: "ChatLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "encrypted_chat_state",
                table: "ChatLogs");
        }
    }
}
