using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profile_AspNetUsers_UserId",
                table: "Profile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Profile",
                table: "Profile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_cse_MarketStatus",
                table: "cse_MarketStatus");

            migrationBuilder.DropPrimaryKey(
                name: "PK_cse_MarketIndices",
                table: "cse_MarketIndices");

            migrationBuilder.RenameTable(
                name: "Profile",
                newName: "Profiles");

            migrationBuilder.RenameTable(
                name: "cse_MarketStatus",
                newName: "CseMarketStatus");

            migrationBuilder.RenameTable(
                name: "cse_MarketIndices",
                newName: "CseMarketIndices");

            migrationBuilder.RenameIndex(
                name: "IX_Profile_UserId",
                table: "Profiles",
                newName: "IX_Profiles_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CseMarketStatus",
                table: "CseMarketStatus",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CseMarketIndices",
                table: "CseMarketIndices",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Profiles_AspNetUsers_UserId",
                table: "Profiles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profiles_AspNetUsers_UserId",
                table: "Profiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Profiles",
                table: "Profiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CseMarketStatus",
                table: "CseMarketStatus");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CseMarketIndices",
                table: "CseMarketIndices");

            migrationBuilder.RenameTable(
                name: "Profiles",
                newName: "Profile");

            migrationBuilder.RenameTable(
                name: "CseMarketStatus",
                newName: "cse_MarketStatus");

            migrationBuilder.RenameTable(
                name: "CseMarketIndices",
                newName: "cse_MarketIndices");

            migrationBuilder.RenameIndex(
                name: "IX_Profiles_UserId",
                table: "Profile",
                newName: "IX_Profile_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Profile",
                table: "Profile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cse_MarketStatus",
                table: "cse_MarketStatus",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cse_MarketIndices",
                table: "cse_MarketIndices",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Profile_AspNetUsers_UserId",
                table: "Profile",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
