using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PortfolioId = table.Column<int>(type: "int", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TotalValueLkr = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    TotalValueUsdt = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    TotalProfitLossLkr = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    TotalProfitLossUsdt = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    UsdtToLkrRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    HoldingCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshots_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortfolioSnapshots_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_PortfolioId_CapturedAt",
                table: "PortfolioSnapshots",
                columns: new[] { "PortfolioId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_UserId_CapturedAt",
                table: "PortfolioSnapshots",
                columns: new[] { "UserId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioSnapshots");
        }
    }
}
