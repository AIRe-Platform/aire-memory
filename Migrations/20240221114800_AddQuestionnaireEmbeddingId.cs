using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aire.Memory.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionnaireEmbeddingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "embedding_id",
                table: "Questionnaires",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding_id",
                table: "Questionnaires");
        }
    }
}
