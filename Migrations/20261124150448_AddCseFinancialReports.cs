using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dotnetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCseFinancialReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CseFinancialReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Symbol = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CseReportId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PdfPath = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SharesInIssue = table.Column<long>(type: "bigint", nullable: true),
                    NavPerShare = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NavFromEquity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NavEntity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NavStatus = table.Column<int>(type: "int", nullable: false),
                    EpsEntity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EpsStatus = table.Column<int>(type: "int", nullable: false),
                    EpsYtdMonths = table.Column<int>(type: "int", nullable: true),
                    EpsYtdCurrent = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsYtdPrior = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsQuarterCurrent = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsQuarterPrior = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EpsLastFullYear = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PublicFloatPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    ExtractorVersion = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExtractedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CseFinancialReports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CseFinancialReports_CseReportId",
                table: "CseFinancialReports",
                column: "CseReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CseFinancialReports_Symbol_PeriodEnd",
                table: "CseFinancialReports",
                columns: new[] { "Symbol", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CseFinancialReports");
        }
    }
}
