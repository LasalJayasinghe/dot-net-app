using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    public partial class CreateCseTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --------------------------
            // cse_MarketStatus table
            // --------------------------
            migrationBuilder.CreateTable(
                name: "cse_MarketStatus",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MarketDate = table.Column<DateTime>(type: "date", nullable: false),
                    StatusCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StatusLabel = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    IsOpen = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    IsTradingDay = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    OpenTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    CloseTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    Reason = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "CSE"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "UTC_TIMESTAMP()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "UTC_TIMESTAMP()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cse_MarketStatus", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cse_MarketStatus_MarketDate",
                table: "cse_MarketStatus",
                column: "MarketDate",
                unique: true);

            // --------------------------
            // cse_MarketIndices table
            // --------------------------
            migrationBuilder.CreateTable(
                name: "cse_MarketIndices",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IndexType = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false), // ASPI / SNP
                    TradeDate = table.Column<DateTime>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HighValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LowValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Change = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "CSE"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "UTC_TIMESTAMP()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "UTC_TIMESTAMP()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cse_MarketIndices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cse_MarketIndices_IndexType_TradeDate",
                table: "cse_MarketIndices",
                columns: new[] { "IndexType", "TradeDate" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "cse_MarketIndices");
            migrationBuilder.DropTable(name: "cse_MarketStatus");
        }
    }
}
