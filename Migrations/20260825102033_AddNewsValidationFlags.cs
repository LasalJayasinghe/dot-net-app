using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsValidationFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsModelValidated",
                table: "NewsArticles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRelevant",
                table: "NewsArticles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_IsModelValidated",
                table: "NewsArticles",
                column: "IsModelValidated");

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_IsRelevant",
                table: "NewsArticles",
                column: "IsRelevant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_IsModelValidated",
                table: "NewsArticles");

            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_IsRelevant",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "IsModelValidated",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "IsRelevant",
                table: "NewsArticles");
        }
    }
}
