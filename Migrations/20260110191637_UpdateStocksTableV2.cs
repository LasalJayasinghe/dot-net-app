using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStocksTableV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTradedAt",
                table: "Stocks");

            migrationBuilder.RenameColumn(
                name: "MarketCap",
                table: "Stocks",
                newName: "PreviousClose");

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingPrice",
                table: "Stocks",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingPrice",
                table: "Stocks");

            migrationBuilder.RenameColumn(
                name: "PreviousClose",
                table: "Stocks",
                newName: "MarketCap");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTradedAt",
                table: "Stocks",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
