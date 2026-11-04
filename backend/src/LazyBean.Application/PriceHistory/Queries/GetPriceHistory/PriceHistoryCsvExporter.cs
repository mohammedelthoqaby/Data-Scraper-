using System.Text;

namespace LazyBean.Application.PriceHistory.Queries.GetPriceHistory;

public static class PriceHistoryCsvExporter
{
    public static byte[] ExportToCsv(PriceHistoryResponseDto history)
    {
        var sb = new StringBuilder();

        // UTF-8 BOM
        sb.Append('\uFEFF');

        // Header
        sb.AppendLine("ObservationId,ObservedAt,RunId,CompetitorKey,CompetitorName,ProductId,ProductTitle,ExternalId,VariantId,VariantTitle,Price,CompareAtPrice,InStock,Currency");

        foreach (var p in history.Items)
        {
            var cols = new List<string>
            {
                p.ObservationId.ToString(),
                p.ObservedAt.ToString("O"),
                p.RunId.ToString(),
                EscapeCsv(p.CompetitorKey),
                EscapeCsv(p.CompetitorName),
                p.ProductId.ToString(),
                EscapeCsv(p.ProductTitle),
                EscapeCsv(p.ExternalId ?? ""),
                p.VariantId.ToString(),
                EscapeCsv(p.VariantTitle ?? ""),
                p.Price.ToString("F2"),
                p.CompareAtPrice?.ToString("F2") ?? "",
                p.InStock.HasValue ? (p.InStock.Value ? "In Stock" : "Out of Stock") : "",
                EscapeCsv(p.Currency)
            };

            sb.AppendLine(string.Join(",", cols));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return $"\"{s.Replace("\"", "\"\"")}\"";
        }
        return s;
    }
}
