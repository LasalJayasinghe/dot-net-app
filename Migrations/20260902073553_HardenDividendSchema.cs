using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <summary>
    /// Reshapes the Dividends table:
    ///  - splits the free-text PaymentDate/AgmDate into parsed date columns plus retained raw text,
    ///  - adds an indexed BaseSymbol so symbol lookups are an equality match,
    ///  - adds CseAnnouncementId so the CSE id is a uniquely indexed attribute rather than the
    ///    auto-increment primary key,
    ///  - corrects RecordDate values that were stored as UTC instants (Colombo midnight became
    ///    18:30 on the previous day) back to the Colombo calendar date they represent,
    ///  - indexes RecordDate and (BaseSymbol, RecordDate) for the dividend queries.
    ///
    /// Hand-edited: the scaffolded version altered varchar(100) straight to datetime, which would
    /// have discarded every existing "25 Sep 2026"-style value.
    /// </summary>
    public partial class HardenDividendSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Preserve the existing free-text dates by renaming rather than retyping.
            migrationBuilder.RenameColumn(
                name: "PaymentDate",
                table: "Dividends",
                newName: "PaymentDateRaw");

            migrationBuilder.RenameColumn(
                name: "AgmDate",
                table: "Dividends",
                newName: "AgmDateRaw");

            // 2. New typed columns.
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDate",
                table: "Dividends",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgmDate",
                table: "Dividends",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseSymbol",
                table: "Dividends",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CseAnnouncementId",
                table: "Dividends",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // 3. Backfill before the indexes exist, so the unique index is not applied to a column
            //    that is still all zeroes.
            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `BaseSymbol` = SUBSTRING_INDEX(`Symbol`, '.', 1);");

            // Existing rows used the CSE announcement id as their primary key, so Id is the id.
            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `CseAnnouncementId` = `Id`;");

            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `PaymentDate` = STR_TO_DATE(`PaymentDateRaw`, '%d %b %Y') " +
                "WHERE `PaymentDateRaw` IS NOT NULL AND STR_TO_DATE(`PaymentDateRaw`, '%d %b %Y') IS NOT NULL;");

            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `AgmDate` = STR_TO_DATE(`AgmDateRaw`, '%d %b %Y') " +
                "WHERE `AgmDateRaw` IS NOT NULL AND STR_TO_DATE(`AgmDateRaw`, '%d %b %Y') IS NOT NULL;");

            // 4. Shift RecordDate from a UTC instant back to the Colombo calendar date. Colombo is
            //    a fixed UTC+05:30, so a stored "08 Sep 18:30" is really the 9th of September.
            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `RecordDate` = DATE(`RecordDate` + INTERVAL 330 MINUTE);");

            // 5. Indexes.
            migrationBuilder.CreateIndex(
                name: "IX_Dividends_BaseSymbol_RecordDate",
                table: "Dividends",
                columns: new[] { "BaseSymbol", "RecordDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Dividends_CseAnnouncementId",
                table: "Dividends",
                column: "CseAnnouncementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dividends_RecordDate",
                table: "Dividends",
                column: "RecordDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Dividends_BaseSymbol_RecordDate",
                table: "Dividends");

            migrationBuilder.DropIndex(
                name: "IX_Dividends_CseAnnouncementId",
                table: "Dividends");

            migrationBuilder.DropIndex(
                name: "IX_Dividends_RecordDate",
                table: "Dividends");

            // Put RecordDate back to the UTC instant the old code wrote.
            migrationBuilder.Sql(
                "UPDATE `Dividends` SET `RecordDate` = `RecordDate` - INTERVAL 330 MINUTE;");

            migrationBuilder.DropColumn(
                name: "PaymentDate",
                table: "Dividends");

            migrationBuilder.DropColumn(
                name: "AgmDate",
                table: "Dividends");

            migrationBuilder.DropColumn(
                name: "BaseSymbol",
                table: "Dividends");

            migrationBuilder.DropColumn(
                name: "CseAnnouncementId",
                table: "Dividends");

            migrationBuilder.RenameColumn(
                name: "PaymentDateRaw",
                table: "Dividends",
                newName: "PaymentDate");

            migrationBuilder.RenameColumn(
                name: "AgmDateRaw",
                table: "Dividends",
                newName: "AgmDate");
        }
    }
}
