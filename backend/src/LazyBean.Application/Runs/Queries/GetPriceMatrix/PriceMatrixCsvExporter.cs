using System.Text;

namespace LazyBean.Application.Runs.Queries.GetPriceMatrix;

public static class PriceMatrixCsvExporter
{
    private static readonly string[] CompetitorKeys =
    [
        "cafelax",
        "coffee-and-cream",
        "fengany",
        "coffee-zone",
        "strong-coffee"
    ];

    public static byte[] ExportToCsv(PriceMatrixResponseDto matrix)
    {
        var sb = new StringBuilder();

        // UTF-8 BOM
        sb.Append('\uFEFF');

        // Headers
        var headers = new List<string>
        {
            "LazyBeanProductId",
            "LazyBeanTitle",
            "Category",
            "CanonicalCategory",
            "Brand",
            "WeightGrams",
            "UnitCount",
            "LazyBeanPrice",
            "LazyBeanVendor",
            "LazyBeanVendorCount",
            "LazyBeanInStock",
            "LazyBeanUrl",
            "CheapestCompetitor",
            "CheapestCompetitorPrice",
            "Position"
        };

        foreach (var key in CompetitorKeys)
        {
            headers.Add($"{key}_Price");
            headers.Add($"{key}_CompareAt");
            headers.Add($"{key}_PricePerGram");
            headers.Add($"{key}_PricePerUnit");
            headers.Add($"{key}_InStock");
            headers.Add($"{key}_ObservedAt");
            headers.Add($"{key}_IsStale");
            headers.Add($"{key}_DiffVsLazyBean");
            headers.Add($"{key}_DiffPercent");
            headers.Add($"{key}_MatchStatus");
            headers.Add($"{key}_Url");
        }

        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        foreach (var row in matrix.Rows)
        {
            var cols = new List<string>
            {
                row.LazyBeanProductId.ToString(),
                EscapeCsv(row.LazyBeanTitle),
                EscapeCsv(row.Category ?? ""),
                EscapeCsv(row.CanonicalCategory),
                EscapeCsv(row.Brand ?? ""),
                row.WeightGrams?.ToString() ?? "",
                row.UnitCount?.ToString() ?? "",
                row.LazyBeanPrice.ToString("F2"),
                EscapeCsv(row.LazyBeanVendor ?? ""),
                row.LazyBeanVendorCount.ToString(),
                row.LazyBeanInStock ? "In Stock" : "Out of Stock",
                EscapeCsv(row.LazyBeanUrl),
                EscapeCsv(row.CheapestCompetitorName ?? ""),
                row.CheapestCompetitorPrice?.ToString("F2") ?? "",
                EscapeCsv(row.Position)
            };

            foreach (var key in CompetitorKeys)
            {
                var hasCell = row.CompetitorPrices.TryGetValue(key, out var cell)
                    || (key == "coffee-zone" && row.CompetitorPrices.TryGetValue("coffee-zone-misr", out cell));

                if (hasCell && cell != null)
                {
                    cols.Add(cell.Price.ToString("F2"));
                    cols.Add(cell.CompareAtPrice?.ToString("F2") ?? "");
                    cols.Add(cell.PricePerGram?.ToString("F4") ?? "");
                    cols.Add(cell.PricePerUnit?.ToString("F4") ?? "");
                    cols.Add(cell.InStock ? "In Stock" : "Out of Stock");
                    cols.Add(cell.ObservedAt.ToString("O"));
                    cols.Add(cell.IsStale ? "Stale" : "Fresh");
                    cols.Add(cell.DifferenceVsLazyBean.ToString("F2"));
                    cols.Add(cell.DifferencePercentageVsLazyBean?.ToString("F2") ?? "");
                    cols.Add(EscapeCsv(cell.MatchStatus));
                    cols.Add(EscapeCsv(cell.CompetitorUrl));
                }
                else
                {
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                    cols.Add("");
                }
            }

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
