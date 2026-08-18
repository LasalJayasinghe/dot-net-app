using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using dotnetApp.Application.Dtos;
using ExcelDataReader;
using System.IO;

namespace dotnetApp.Application.Services
{
    public interface IPortfolioFileSyncService
    {
        List<ParsedHoldingDto> ParsePdf(Stream pdfStream);
        List<ParsedHoldingDto> ParseExcel(Stream excelStream);
    }

    public class PortfolioFileSyncService : IPortfolioFileSyncService
    {
        // Matches CSE symbols like AEL.N0000, JKH.N0000, etc.
        private static readonly Regex SymbolRegex = new Regex(@"^[A-Z0-9]{3,4}\.[A-Z0-9]{5}$", RegexOptions.Compiled);

        public List<ParsedHoldingDto> ParsePdf(Stream pdfStream)
        {
            var holdings = new List<ParsedHoldingDto>();

            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    var words = page.GetWords();

                    // Group words by approximate Y coordinate (bottom of text bounding box)
                    var lines = words
                        .GroupBy(w => Math.Round(w.BoundingBox.Bottom))
                        .OrderByDescending(g => g.Key)
                        .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
                        .ToList();

                    foreach (var line in lines)
                    {
                        if (line.Count < 5) continue;

                        var firstWord = line[0].Text;
                        if (SymbolRegex.IsMatch(firstWord))
                        {
                            try
                            {
                                var lineText = string.Join(" ", line.Select(w => w.Text));
                                var parts = lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                if (parts.Length >= 8)
                                {
                                    var symbol = parts[0];
                                    
                                    var qtyStr = parts[1].Replace(",", "");
                                    if (!decimal.TryParse(qtyStr, out var quantity)) continue;

                                    var priceStr = parts[7].Replace(",", "");
                                    if (!decimal.TryParse(priceStr, out var avgPrice)) continue;

                                    holdings.Add(new ParsedHoldingDto
                                    {
                                        Symbol = symbol,
                                        Quantity = quantity,
                                        AverageBuyPrice = avgPrice
                                    });
                                }
                            }
                            catch { /* Skip malformed rows */ }
                        }
                    }
                }
            }

            return holdings;
        }

        public List<ParsedHoldingDto> ParseExcel(Stream excelStream)
        {
            var holdings = new List<ParsedHoldingDto>();

            using (var reader = ExcelReaderFactory.CreateReader(excelStream))
            {
                do
                {
                    while (reader.Read())
                    {
                        try
                        {
                            var firstCell = reader.GetValue(0)?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(firstCell) && SymbolRegex.IsMatch(firstCell))
                            {
                                var qtyObj = reader.GetValue(1);
                                var avgPriceObj = reader.GetValue(7);

                                if (qtyObj != null && avgPriceObj != null)
                                {
                                    if (decimal.TryParse(qtyObj.ToString(), out var quantity) &&
                                        decimal.TryParse(avgPriceObj.ToString(), out var avgPrice))
                                    {
                                        holdings.Add(new ParsedHoldingDto
                                        {
                                            Symbol = firstCell,
                                            Quantity = quantity,
                                            AverageBuyPrice = avgPrice
                                        });
                                    }
                                }
                            }
                        }
                        catch { /* Skip malformed rows */ }
                    }
                } while (reader.NextResult());
            }

            return holdings;
        }
    }
}
