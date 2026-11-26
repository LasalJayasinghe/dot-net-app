using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioCashAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashBalance",
                table: "Portfolios",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PortfolioTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PortfolioId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Symbol = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Fees = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    CashChange = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    CashBalanceAfter = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    RealizedProfitLoss = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_PortfolioId_ExecutedAt",
                table: "PortfolioTransactions",
                columns: new[] { "PortfolioId", "ExecutedAt" });

            // Start every existing position's history with an "Opening" entry, so the ledger accounts
            // for holdings that predate it. No cash moves: cash starts at 0 until the user sets it.
            migrationBuilder.Sql(@"
                INSERT INTO PortfolioTransactions
                    (PortfolioId, Type, Symbol, Quantity, Price, Fees, CashChange, CashBalanceAfter,
                     RealizedProfitLoss, Notes, ExecutedAt, CreatedAt)
                SELECT h.PortfolioId, 'Opening', h.Symbol, h.Quantity, h.AverageBuyPrice, 0, 0, 0,
                       NULL, 'Holding before transaction tracking started', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
                FROM PortfolioHoldings h;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioTransactions");

            migrationBuilder.DropColumn(
                name: "CashBalance",
                table: "Portfolios");
        }
    }
}
