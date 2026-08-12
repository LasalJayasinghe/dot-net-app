using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using dotnetApp.Application.Dtos;

namespace dotnetApp.Application.Services
{
    public interface IPdfSyncService
    {
        List<ParsedHoldingDto> ParseATradPortfolio(Stream pdfStream);
    }

    public class PdfSyncService : IPdfSyncService
    {
        // Matches CSE symbols like AEL.N0000, JKH.N0000, etc.
        private static readonly Regex SymbolRegex = new Regex(@"^[A-Z0-9]{3,4}\.[A-Z0-9]{5}$", RegexOptions.Compiled);

        public List<ParsedHoldingDto> ParseATradPortfolio(Stream pdfStream)
        {
            var holdings = new List<ParsedHoldingDto>();

            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    var words = page.GetWords();

                    // Group words by approximate Y coordinate (bottom of text bounding box)
                    // This perfectly reconstructs rows, avoiding column text interweaving
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
                                // Reconstruct the line as a space-separated string
                                var lineText = string.Join(" ", line.Select(w => w.Text));
                                var parts = lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                // Expected positions: [0] Symbol, [1] Quantity, ..., [7] AvgPrice
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
                            catch
                            {
                                // Skip malformed rows
                            }
                        }
                    }
                }
            }

            return holdings;
        }
    }
}
