using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddDeskAlertsAndCryptoWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Market",
                table: "WatchlistItems",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Cse")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "FireOnce",
                table: "Alerts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTriggeredAt",
                table: "Alerts",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Market",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "FireOnce",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "LastTriggeredAt",
                table: "Alerts");
        }
    }
}
