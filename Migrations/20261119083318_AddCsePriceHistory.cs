using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCsePriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CseIndexDaily",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IndexCode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Low = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Change = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ChangePercent = table.Column<decimal>(type: "decimal(12,6)", nullable: true),
                    Source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CseIndexDaily", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CseMarketDaily",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MarketTurnover = table.Column<decimal>(type: "decimal(22,2)", nullable: true),
                    MarketTrades = table.Column<long>(type: "bigint", nullable: true),
                    EquityForeignPurchase = table.Column<decimal>(type: "decimal(22,2)", nullable: true),
                    EquityForeignSales = table.Column<decimal>(type: "decimal(22,2)", nullable: true),
                    MarketCap = table.Column<decimal>(type: "decimal(26,2)", nullable: true),
                    ListedCompanies = table.Column<int>(type: "int", nullable: true),
                    TradedCompanies = table.Column<int>(type: "int", nullable: true),
                    Asi = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SpSl20 = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Per = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Pbv = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    DividendYield = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    RawJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CseMarketDaily", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CsePriceDaily",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Symbol = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StockId = table.Column<int>(type: "int", nullable: true),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    High = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PreviousClose = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ShareVolume = table.Column<long>(type: "bigint", nullable: false),
                    Trades = table.Column<long>(type: "bigint", nullable: true),
                    Turnover = table.Column<decimal>(type: "decimal(22,2)", nullable: true),
                    MarketCap = table.Column<decimal>(type: "decimal(24,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CsePriceDaily", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CseSecurities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Symbol = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstSeen = table.Column<DateOnly>(type: "date", nullable: false),
                    LastSeen = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CseSecurities", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CseIndexDaily_IndexCode_TradeDate",
                table: "CseIndexDaily",
                columns: new[] { "IndexCode", "TradeDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CseMarketDaily_TradeDate",
                table: "CseMarketDaily",
                column: "TradeDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CsePriceDaily_Symbol_TradeDate",
                table: "CsePriceDaily",
                columns: new[] { "Symbol", "TradeDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CsePriceDaily_TradeDate",
                table: "CsePriceDaily",
                column: "TradeDate");

            migrationBuilder.CreateIndex(
                name: "IX_CseSecurities_Symbol",
                table: "CseSecurities",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CseIndexDaily");

            migrationBuilder.DropTable(
                name: "CseMarketDaily");

            migrationBuilder.DropTable(
                name: "CsePriceDaily");

            migrationBuilder.DropTable(
                name: "CseSecurities");
        }
    }
}
