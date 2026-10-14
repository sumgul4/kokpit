using System.Drawing;
using System.Globalization;

namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitMetrics.cs — every number the cards show, calculated from the (already filtered) action rows.
//  Pure functions: no database, no UI. Tabs call these and bind the results to charts / grids.
//
//  Grain reminder: rows are ACTIONS. Finding counts always go through FindingRows() (one row per FindingId),
//  because finding-level columns repeat on every action row of the same finding.
// =====================================================================================================

/// <summary>One bar / segment of a chart. Series = stacked series name (empty for single-series charts).</summary>
public sealed record KokpitChartPoint(string Argument, string Series, int Value);

public sealed record KokpitKpi(string Key, string Title, string Value, string? Note = null, string? Tone = null);

public sealed record KokpitHeatmap(
    IReadOnlyList<string> Rows,
    IReadOnlyList<string> Columns,
    IReadOnlyDictionary<(string Row, string Column), int> Cells,
    IReadOnlyDictionary<string, int> RowTotals,
    IReadOnlyDictionary<string, int> ColumnTotals,
    int Total,
    int Max)
{
    public int this[string row, string column] => Cells.TryGetValue((row, column), out var v) ? v : 0;
}

public sealed record KokpitDueItem(
    int ActionId, string? FindingCode, string? ActionSummary, string Organization, DateTime DueDate, int DaysLeft, int CompletionRate);

public sealed record KokpitPostponedItem(
    int ActionId, string? FindingCode, string? ActionSummary, string Organization, string FindingLevel,
    int RevisionCount, DateTime? DueDate, int OverdueDays);

public static class KokpitMetrics
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // ---------- helpers ----------

    /// <summary>One row per finding (finding-level columns are identical on all its action rows).</summary>
    public static IEnumerable<KokpitAction> FindingRows(this IEnumerable<KokpitAction> actions) =>
        actions.GroupBy(a => a.FindingId).Select(g => g.First());

    public static IEnumerable<KokpitAction> Open(this IEnumerable<KokpitAction> actions) =>
        actions.Where(a => !a.IsActionCompleted);

    private static string N(int value) => value.ToString("N0", Tr);

    // ---------- 1. KPI row ----------

    public static IReadOnlyList<KokpitKpi> Kpis(IReadOnlyList<KokpitAction> actions, DateTime today)
    {
        var findings = actions.FindingRows().ToList();
        var openFindings = findings.Where(f => !f.IsFindingCompleted).ToList();
        var openHigh = openFindings.Count(f => f.FindingLevel == "Yüksek");

        var avgCompletion = findings.Count == 0 ? 0m : findings.Average(f => f.FindingCompletionRate);

        var overdue = actions.Where(a => a.IsOverdue(today)).ToList();
        var overdue90 = overdue.Count(a => a.OverdueDays(today) > 90);

        var completed = actions.Where(a => a.IsActionCompleted && a.CompletionDate is not null).ToList();
        var withDue = completed.Where(a => a.MaxUndertakingDate is not null).ToList();
        var onTime = withDue.Count(a => a.CompletionDate!.Value.Date <= a.MaxUndertakingDate!.Value.Date);

        var closeDays = completed.Where(a => a.FindingDate is not null)
            .Select(a => (a.CompletionDate!.Value.Date - a.FindingDate!.Value.Date).TotalDays)
            .Where(d => d >= 0).ToList();

        var notAgreed = actions.Open().Count(a => a.ActionConsensus == "Kabul Etmiyor");

        return
        [
            new("open-findings", "Açık bulgu", N(openFindings.Count),
                $"{N(openHigh)} yüksek seviyeli · toplam {N(findings.Count)}", openHigh > 0 ? "risk" : null),
            new("completion", "Ort. bulgu tamamlanma", "%" + avgCompletion.ToString("0", Tr),
                "sorumluluk yüzdesine göre ağırlıklı"),
            new("overdue", "Gecikmiş aksiyon", N(overdue.Count),
                $"{N(overdue90)} tanesi 90+ gün", overdue.Count > 0 ? "risk" : "ok"),
            new("on-time", "Zamanında kapanma",
                withDue.Count == 0 ? "–" : "%" + (100.0 * onTime / withDue.Count).ToString("0", Tr),
                $"{N(onTime)} / {N(withDue.Count)} tamamlanan aksiyon"),
            new("close-days", "Ort. kapanma süresi",
                closeDays.Count == 0 ? "–" : closeDays.Average().ToString("0", Tr) + " gün",
                "bulgu tarihinden tamamlanmaya"),
            new("not-agreed", "Mutabakat yok", N(notAgreed),
                "açık aksiyon · \"Kabul Etmiyor\"", notAgreed > 0 ? "warn" : null)
        ];
    }

    // ---------- 2. Sector × finding level heatmap (open findings) ----------

    public static KokpitHeatmap SectorLevelHeatmap(IReadOnlyList<KokpitAction> actions)
    {
        // A finding with actions in two sectors counts once in each sector row, once in the grand total.
        var open = actions.Where(a => !a.IsFindingCompleted).ToList();

        var cells = open
            .GroupBy(a => (Row: KokpitDimensions.Text(a.Sector), Column: KokpitDimensions.Text(a.FindingLevel)))
            .ToDictionary(g => g.Key, g => g.Select(a => a.FindingId).Distinct().Count());

        var rowTotals = open.GroupBy(a => KokpitDimensions.Text(a.Sector))
            .ToDictionary(g => g.Key, g => g.Select(a => a.FindingId).Distinct().Count());
        var columnTotals = open.GroupBy(a => KokpitDimensions.Text(a.FindingLevel))
            .ToDictionary(g => g.Key, g => g.Select(a => a.FindingId).Distinct().Count());

        var rows = rowTotals.OrderBy(p => p.Key == KokpitCodes.Unspecified).ThenByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Create(Tr, false))
            .Select(p => p.Key).ToList();
        var columns = Ordered(columnTotals.Keys, KokpitCodes.FindingLevels);

        return new KokpitHeatmap(rows, columns, cells, rowTotals, columnTotals,
            open.Select(a => a.FindingId).Distinct().Count(),
            cells.Count == 0 ? 0 : cells.Values.Max());
    }

    // ---------- 3. Overdue aging (overdue actions by bucket, stacked by finding level) ----------

    public static IReadOnlyList<KokpitChartPoint> OverdueAging(IReadOnlyList<KokpitAction> actions, DateTime today)
    {
        var overdue = actions.Where(a => a.IsOverdue(today)).ToList();
        return Cross(overdue,
            a => KokpitDimensions.OverdueBucket(a, today)!, KokpitDimensions.OverdueBuckets,
            a => KokpitDimensions.Text(a.FindingLevel), KokpitCodes.FindingLevels);
    }

    // ---------- 4. Upcoming due dates ----------

    public static IReadOnlyList<(string Window, int Count)> DueWindowCounts(IReadOnlyList<KokpitAction> actions, DateTime today) =>
        KokpitDimensions.DueWindows
            .Select(w => (w, actions.Count(a => KokpitDimensions.DueWindow(a, today) == w)))
            .ToList();

    public static IReadOnlyList<KokpitDueItem> NextDue(IReadOnlyList<KokpitAction> actions, DateTime today, int take = 8) =>
        actions.Where(a => KokpitDimensions.DueWindow(a, today) is not null)
            .OrderBy(a => a.MaxUndertakingDate)
            .Take(take)
            .Select(a => new KokpitDueItem(a.ActionId, a.FindingCode, a.ActionSummary,
                KokpitDimensions.Text(a.ResponsibleOrganizationName), a.MaxUndertakingDate!.Value.Date,
                (a.MaxUndertakingDate!.Value.Date - today.Date).Days, a.CompletionRate ?? 0))
            .ToList();

    // ---------- 5. Open actions by responsible unit (top N, overdue vs not yet due) ----------

    public const string SeriesOverdue = "Gecikmiş";
    public const string SeriesNotDue = "Vadesi gelmemiş";

    public static IReadOnlyList<KokpitChartPoint> OpenByOrganization(IReadOnlyList<KokpitAction> actions, DateTime today, int top = 10)
    {
        var open = actions.Open().ToList();
        var topOrgs = open.GroupBy(a => KokpitDimensions.Text(a.ResponsibleOrganizationName))
            .OrderBy(g => g.Key == KokpitCodes.Unspecified).ThenByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(top).Select(g => g.Key).ToList();

        // Rotated chart draws the first argument at the bottom → reverse so the largest is on top.
        topOrgs.Reverse();

        return Cross(open.Where(a => topOrgs.Contains(KokpitDimensions.Text(a.ResponsibleOrganizationName))),
            a => KokpitDimensions.Text(a.ResponsibleOrganizationName), topOrgs,
            a => a.IsOverdue(today) ? SeriesOverdue : SeriesNotDue, [SeriesOverdue, SeriesNotDue]);
    }

    // ---------- 6. Monthly opened vs completed (last 12 months) ----------

    public const string SeriesOpened = "Açılan";
    public const string SeriesCompleted = "Tamamlanan";

    public static IReadOnlyList<KokpitChartPoint> MonthlyFlow(IReadOnlyList<KokpitAction> actions, DateTime today, int months = 12)
    {
        var first = new DateTime(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var result = new List<KokpitChartPoint>();
        for (var m = first; m <= today; m = m.AddMonths(1))
        {
            var label = m.ToString("MMM yy", Tr);
            var next = m.AddMonths(1);
            // Opened = the finding was raised that month (one action = one row).
            result.Add(new(label, SeriesOpened, actions.Count(a => a.FindingDate >= m && a.FindingDate < next)));
            result.Add(new(label, SeriesCompleted,
                actions.Count(a => a.IsActionCompleted && a.CompletionDate >= m && a.CompletionDate < next)));
        }
        return result;
    }

    // ---------- 7. Urgency × importance (open actions) ----------

    public static IReadOnlyList<KokpitChartPoint> UrgencyImportance(IReadOnlyList<KokpitAction> actions) =>
        Cross(actions.Open().ToList(),
            a => KokpitDimensions.Text(a.ActionUrgency), KokpitCodes.ActionUrgencies,
            a => KokpitDimensions.Text(a.ActionImportance), KokpitCodes.ActionImportances);

    // ---------- 8. Most postponed open actions ----------

    public static IReadOnlyList<KokpitPostponedItem> MostPostponed(IReadOnlyList<KokpitAction> actions, DateTime today, int take = 10) =>
        actions.Open()
            .Where(a => (a.RevisionCount ?? 0) > 0)
            .OrderByDescending(a => a.RevisionCount).ThenByDescending(a => a.OverdueDays(today))
            .Take(take)
            .Select(a => new KokpitPostponedItem(a.ActionId, a.FindingCode, a.ActionSummary,
                KokpitDimensions.Text(a.ResponsibleOrganizationName), KokpitDimensions.Text(a.FindingLevel),
                a.RevisionCount ?? 0, a.MaxUndertakingDate, a.OverdueDays(today)))
            .ToList();

    // ---------- shared ----------

    /// <summary>
    /// Argument × series counts with every combination present (zeros included), so stacked bars keep the
    /// argument order. Known codes come first in their display order; unknown values (incl. "Belirtilmemiş") follow.
    /// </summary>
    private static IReadOnlyList<KokpitChartPoint> Cross(
        IEnumerable<KokpitAction> rows,
        Func<KokpitAction, string> argument, IReadOnlyList<string> argumentOrder,
        Func<KokpitAction, string> series, IReadOnlyList<string> seriesOrder)
    {
        var counts = rows.GroupBy(a => (Arg: argument(a), Ser: series(a))).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0) return [];

        var args = Ordered(counts.Keys.Select(k => k.Arg), argumentOrder, keepAllKnown: true);
        var sers = Ordered(counts.Keys.Select(k => k.Ser), seriesOrder);

        return (from s in sers
                from a in args
                select new KokpitChartPoint(a, s, counts.TryGetValue((a, s), out var v) ? v : 0)).ToList();
    }

    /// <summary>Known values in display order, then the rest alphabetically.</summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> present, IReadOnlyList<string> known, bool keepAllKnown = false)
    {
        var set = present.ToHashSet();
        var head = known.Where(k => keepAllKnown || set.Contains(k));
        var tail = set.Except(known).OrderBy(v => v == KokpitCodes.Unspecified).ThenBy(v => v, StringComparer.Create(Tr, false));
        return head.Concat(tail).ToList();
    }
}

/// <summary>Colours used by the charts (finding level = severity; same colours in light and dark theme).</summary>
public static class KokpitPalette
{
    public static Color For(string series) => series switch
    {
        "Yüksek" => ColorTranslator.FromHtml("#c8372d"),
        "Orta" => ColorTranslator.FromHtml("#e07b39"),
        "Düşük" => ColorTranslator.FromHtml("#d9a91e"),
        "Gelişim Fırsatı" => ColorTranslator.FromHtml("#3c7fc0"),
        "Bilgi" => ColorTranslator.FromHtml("#8995a3"),
        KokpitMetrics.SeriesOverdue => ColorTranslator.FromHtml("#c8372d"),
        KokpitMetrics.SeriesNotDue => ColorTranslator.FromHtml("#2f8f7c"),
        KokpitMetrics.SeriesOpened => ColorTranslator.FromHtml("#9aa6b2"),
        KokpitMetrics.SeriesCompleted => ColorTranslator.FromHtml("#0f6b5c"),
        _ => ColorTranslator.FromHtml("#b7c0c9")   // Belirtilmemiş and anything unknown
    };
}
