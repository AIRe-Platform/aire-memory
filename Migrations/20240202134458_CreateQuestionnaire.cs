using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aire.Memory.Migrations
{
    /// <inheritdoc />
    public partial class CreateQuestionnaire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Questionnaires",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    lang = table.Column<string>(type: "text", nullable: true),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    keywords = table.Column<string[]>(type: "text[]", nullable: true),
                    preliminary = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaires", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Questionnaires");
        }
    }
}
