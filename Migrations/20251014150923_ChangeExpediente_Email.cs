using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotApp.Migrations
{
    /// <inheritdoc />
    public partial class ChangeExpediente_Email : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "bot",
                table: "Expedientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                schema: "bot",
                table: "Expedientes");
        }
    }
}
