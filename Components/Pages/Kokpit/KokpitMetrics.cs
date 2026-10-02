using System.Drawing;
using System.Globalization;
using DevExpress.Blazor;

namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitMetrics.cs — turns KokpitView into chart-ready rows. Pure functions, no UI, no database.
//    *Calculator   one static class per page
//    Formatting    tr-TR numbers, dates, month names
//    ChartPalette  chart colours (keep in sync with KokpitLayout.razor.css)
//    ChartInterop  the only code that reads DevExpress chart point objects
// =====================================================================================================

/// <summary>The six headline numbers shown in the KPI row.</summary>
public static class KpiCalculator
{
    public static KpiSummary Calculate(KokpitView data, KokpitOptions options)
    {
        var asOf = data.AsOf.ToDateTime(TimeOnly.MinValue);

        // "Due" = the planned report issue date has passed (definition to be confirmed, see design doc).
        var due = data.Audits.Where(a => (a.PlannedIssueDate ?? a.PlannedEnd.AddDays(options.CycleTimeTargetDays)) <= asOf).ToList();
        var issuedDue = due.Count(a => a.Stage == AuditStages.Issued);

        var ongoing = data.Audits.Count(a => a.Stage is AuditStages.Fieldwork or AuditStages.Reporting);
        var delayed = data.Audits.Count(a => a.IsDelayed && a.Stage != AuditStages.Issued);

        var open = data.Findings.Where(f => f.IsOpen).ToList();
        var overdue = open.Select(f => f.OverdueDays(data.AsOf)).Where(d => d > 0).ToList();

        var cycles = data.Reports.Where(r => r.CycleDays.HasValue).Select(r => (double)r.CycleDays!.Value).ToList();

        var joined = data.Movements.Count(m => m.MovementType == MovementTypes.Joined);
        var left = data.Movements.Count(m => m.MovementType != MovementTypes.Joined);

        return new KpiSummary(
            PlanCompletionRatio: due.Count == 0 ? 0 : (double)issuedDue / due.Count,
            IssuedDueCount: issuedDue,
            DueCount: due.Count,
            OngoingAudits: ongoing,
            DelayedAudits: delayed,
            OpenFindings: open.Count,
            OpenHighRiskFindings: open.Count(f => f.Risk == RiskLevels.High),
            OverdueActions: overdue.Count,
            OverdueOver90: overdue.Count(d => d > 90),
            AverageCycleDays: cycles.Count == 0 ? null : cycles.Average(),
            CycleTargetDays: options.CycleTimeTargetDays,
            Headcount: data.Staff.Count,
            Joined: joined,
            Left: left);
    }
}

/// <summary>Audit plan page: stages, plan vs actual, delays, Gantt.</summary>
public static class AuditPlanCalculator
{
    /// <summary>Areas ordered by total audit count in the UNFILTERED snapshot, so the axis order stays stable while filtering.</summary>
    public static IReadOnlyList<string> AreaOrder(KokpitSnapshot snapshot) =>
        snapshot.Audits.GroupBy(a => a.Area).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    public static IReadOnlyList<StackedValue> StageByArea(KokpitView data, IReadOnlyList<string> areaOrder) =>
        (from area in areaOrder
         from stage in AuditStages.All
         select new StackedValue(area, stage, data.Audits.Count(a => a.Area == area && a.Stage == stage)))
        .ToList();

    public static IReadOnlyList<(string Stage, int Count, int Delayed, double Share)> StageSummary(KokpitView data)
    {
        var total = Math.Max(1, data.Audits.Count);
        return AuditStages.All
            .Select(stage =>
            {
                var items = data.Audits.Where(a => a.Stage == stage).ToList();
                return (stage, items.Count, items.Count(a => a.IsDelayed), (double)items.Count / total);
            })
            .ToList();
    }

    /// <summary>Cumulative planned vs actually issued reports per month of the current year.</summary>
    public static IReadOnlyList<MonthlyValue> PlanVsActual(KokpitView data, int cycleTargetDays)
    {
        var year = data.AsOf.Year;
        var planned = new int[12];
        var actual = new int[12];

        foreach (var audit in data.Audits)
        {
            var plannedIssue = audit.PlannedIssueDate ?? audit.PlannedEnd.AddDays(cycleTargetDays);
            if (plannedIssue.Year == year) planned[plannedIssue.Month - 1]++;
        }
        foreach (var report in data.Reports)
        {
            if (report.IssuedAt is { } issued && issued.Year == year) actual[issued.Month - 1]++;
        }

        var result = new List<MonthlyValue>(24);
        int plannedSum = 0, actualSum = 0;
        for (var m = 1; m <= 12; m++)
        {
            plannedSum += planned[m - 1];
            actualSum += actual[m - 1];
            var label = Formatting.MonthShort(m);
            result.Add(new MonthlyValue(m, label, "Plan (kümülatif)", plannedSum));
            result.Add(new MonthlyValue(m, label, "Gerçekleşen", m <= data.AsOf.Month ? actualSum : null));
        }
        return result;
    }

    public static IReadOnlyList<CategoryValue> DelayedByArea(KokpitView data, IReadOnlyList<string> areaOrder) =>
        areaOrder
            .Select(area => new CategoryValue(area,
                data.Audits.Count(a => a.Area == area && a.IsDelayed && a.Stage != AuditStages.Issued), area))
            .ToList();

    /// <summary>Audits not yet issued, delayed ones first. Label carries the id so duplicate names stay separate rows.</summary>
    public static IReadOnlyList<GanttItem> Gantt(KokpitView data, int take = 16) =>
        data.Audits
            .Where(a => a.Stage != AuditStages.Issued)
            .OrderByDescending(a => a.IsDelayed)
            .ThenBy(a => a.PlannedStart)
            .Take(take)
            .Select(a => new GanttItem(a.AuditId, $"{(a.IsDelayed ? "▲ " : "")}{a.AuditName} · {a.AuditId}",
                a.PlannedStart, a.PlannedEnd, a.Stage, a.IsDelayed))
            .ToList();

    public static int NotIssuedCount(KokpitView data) => data.Audits.Count(a => a.Stage != AuditStages.Issued);
}

/// <summary>Report tracking: step waits vs SLA, cycle time, longest waiting reports.</summary>
public static class ReportFlowCalculator
{
    public static IReadOnlyList<StepStat> StepStats(KokpitView data, KokpitOptions options)
    {
        var waitsByStep = data.ReportSteps
            .Where(s => s.WaitDays.HasValue)
            .GroupBy(s => s.StepCode)
            .ToDictionary(g => g.Key, g => g.Select(s => (double)s.WaitDays!.Value).ToList());

        var waitingByStep = data.Reports
            .Where(r => r.CurrentStepCode is not null)
            .GroupBy(r => r.CurrentStepCode!)
            .ToDictionary(g => g.Key, g => g.Count());

        return data.Steps
            .Select(step =>
            {
                var waits = waitsByStep.GetValueOrDefault(step.StepCode) ?? [];
                var average = waits.Count == 0 ? 0 : waits.Average();
                var status = waits.Count == 0 ? SlaStatus.NoData : Classify(average, step.SlaDays, options.SlaWarningRatio);
                return new StepStat(step.StepCode, step.StepName, step.SlaDays,
                    waitingByStep.GetValueOrDefault(step.StepCode), average, waits.Count, status);
            })
            .ToList();
    }

    public static SlaStatus Classify(double days, int slaDays, double warningRatio)
    {
        if (slaDays <= 0) return SlaStatus.NoData;
        var ratio = days / slaDays;
        return ratio <= 1 ? SlaStatus.OnTime : ratio <= warningRatio ? SlaStatus.Warning : SlaStatus.Critical;
    }

    /// <summary>Average cycle time of reports issued in each month of the current year.</summary>
    public static IReadOnlyList<MonthlyValue> CycleTimeByMonth(KokpitView data) =>
        data.Reports
            .Where(r => r.CycleDays.HasValue && r.IssuedAt?.Year == data.AsOf.Year)
            .GroupBy(r => r.IssuedAt!.Value.Month)
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyValue(g.Key, Formatting.MonthShort(g.Key), "Ortalama çevrim süresi",
                Math.Round(g.Average(r => r.CycleDays!.Value), 1)))
            .ToList();

    public static IReadOnlyList<WaitingReport> LongestWaiting(KokpitView data, KokpitOptions options, int take = 8)
    {
        var sla = data.Steps.ToDictionary(s => s.StepCode, s => s.SlaDays);
        var asOf = data.AsOf.ToDateTime(TimeOnly.MinValue);

        return data.Reports
            .Where(r => r.CurrentStepCode is not null && r.CurrentStepEnteredAt.HasValue)
            .Select(r =>
            {
                var days = Math.Max(0, (int)(asOf - r.CurrentStepEnteredAt!.Value.Date).TotalDays);
                var slaDays = sla.GetValueOrDefault(r.CurrentStepCode!);
                return new WaitingReport(r.ReportId, r.AuditName, r.CurrentStepCode!, days, slaDays,
                    Classify(days, slaDays, options.SlaWarningRatio));
            })
            .OrderByDescending(w => w.SlaDays == 0 ? 0 : (double)w.WaitingDays / w.SlaDays)
            .Take(take)
            .ToList();
    }

    public static double SkippedStepRatio(KokpitView data) =>
        data.Reports.Count == 0 ? 0 : (double)data.Reports.Count(r => r.HasSkippedSteps) / data.Reports.Count;
}

/// <summary>Findings page: heatmap, aging, status, trend, repeats.</summary>
public static class FindingCalculator
{
    /// <summary>Owners ordered by open finding count in the unfiltered snapshot (stable row order).</summary>
    public static IReadOnlyList<string> OwnerOrder(KokpitSnapshot snapshot) =>
        snapshot.Findings.Where(f => f.IsOpen).GroupBy(f => f.ActionOwner)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    public static IReadOnlyList<HeatmapCell> OwnerRiskHeatmap(KokpitView data, IReadOnlyList<string> ownerOrder)
    {
        var counts = data.Findings.Where(f => f.IsOpen)
            .GroupBy(f => (f.ActionOwner, f.Risk))
            .ToDictionary(g => g.Key, g => g.Count());
        var max = Math.Max(1, counts.Count == 0 ? 1 : counts.Values.Max());

        string[] columns = [RiskLevels.Low, RiskLevels.Medium, RiskLevels.High];
        return (from owner in ownerOrder
                from risk in columns
                let count = counts.GetValueOrDefault((owner, risk))
                select new HeatmapCell(owner, risk, count, (double)count / max))
            .ToList();
    }

    public static IReadOnlyList<StackedValue> AgingByRisk(KokpitView data) =>
        (from bucket in AgingBuckets.All
         from risk in RiskLevels.All
         select new StackedValue(bucket, risk, data.Findings.Count(f => f.AgingBucket == bucket && f.Risk == risk)))
        .ToList();

    public static IReadOnlyList<CategoryValue> StatusCounts(KokpitView data) =>
        FindingStatuses.All
            .Select(s => new CategoryValue(DisplayNames.FindingStatus(s), data.Findings.Count(f => f.Status == s), s))
            .ToList();

    public static IReadOnlyList<MonthlyValue> OpenedClosedByMonth(KokpitView data)
    {
        var year = data.AsOf.Year;
        var result = new List<MonthlyValue>();
        for (var m = 1; m <= data.AsOf.Month; m++)
        {
            var label = Formatting.MonthShort(m);
            result.Add(new MonthlyValue(m, label, "Açılan",
                data.Findings.Count(f => f.ReportDate.Year == year && f.ReportDate.Month == m)));
            result.Add(new MonthlyValue(m, label, "Kapanan",
                data.Findings.Count(f => f.ClosedDate?.Year == year && f.ClosedDate?.Month == m)));
        }
        return result;
    }

    public static IReadOnlyList<CategoryValue> RepeatByOwner(KokpitView data, int take = 8) =>
        data.Findings.Where(f => f.IsRepeat)
            .GroupBy(f => f.ActionOwner)
            .Select(g => new CategoryValue(g.Key, g.Count(), g.Key))
            .OrderByDescending(c => c.Value)
            .Take(take)
            .ToList();
}

/// <summary>HR page. Applies the KVKK minimum group size where individuals could be recognised.</summary>
public static class HrCalculator
{
    public static IReadOnlyList<WaterfallStep> HeadcountWaterfall(KokpitView data)
    {
        var joined = data.Movements.Count(m => m.MovementType == MovementTypes.Joined);
        var transferred = data.Movements.Count(m => m.MovementType == MovementTypes.InternalTransfer);
        var left = data.Movements.Count(m => m.MovementType == MovementTypes.Left);
        var current = data.Staff.Count;
        var start = current - joined + transferred + left;

        return
        [
            new("Dönem başı", 0, start, start.ToString(), null),
            new("Yeni katılım", start, joined, $"+{joined}", MovementTypes.Joined),
            new("İç rotasyon", start + joined - transferred, transferred, $"−{transferred}", MovementTypes.InternalTransfer),
            new("Ayrılış", start + joined - transferred - left, left, $"−{left}", MovementTypes.Left),
            new("Güncel", 0, current, current.ToString(), null)
        ];
    }

    public static IReadOnlyList<CategoryValue> TitleDistribution(KokpitView data, KokpitOptions options) =>
        Ordered(data.Staff.Select(s => s.Title), options.TitleOrder)
            .Select(title => new CategoryValue(title, data.Staff.Count(s => s.Title == title), title))
            .ToList();

    public static IReadOnlyList<FlowLink> MobilityLinks(KokpitView data, KokpitOptions options) =>
        data.Movements
            .GroupBy(m => m.MovementType == MovementTypes.Joined
                ? (Source: m.MovementDetail, Target: options.BoardName)
                : (Source: options.BoardName, Target: m.MovementDetail))
            .Select(g => new FlowLink(g.Key.Source, g.Key.Target, g.Count()))
            .ToList();

    /// <summary>Promotion counts per type; the average score is hidden when the group is smaller than MinGroupSize.</summary>
    public static IReadOnlyList<(string Type, int Count, double? AverageScore)> PromotionsByType(
        KokpitView data, KokpitOptions options) =>
        PromotionTypes.All
            .Select(type =>
            {
                var items = data.Promotions.Where(p => p.PromotionType == type).ToList();
                double? average = items.Count >= options.MinGroupSize ? (double)items.Average(p => p.DevelopmentScore) : null;
                return (type, items.Count, average);
            })
            .ToList();

    /// <summary>
    /// One point per promotion. KVKK: titles with fewer promotions than MinGroupSize are left out
    /// and returned in <c>HiddenTitles</c> so the UI can say so.
    /// </summary>
    public static (IReadOnlyList<ScorePoint> Points, IReadOnlyList<string> HiddenTitles) ScoreVsPromotion(
        KokpitView data, KokpitOptions options)
    {
        var byTitle = data.Promotions.GroupBy(p => p.ToTitle).ToList();
        var hidden = byTitle.Where(g => g.Count() < options.MinGroupSize).Select(g => g.Key).ToList();
        var points = byTitle.Where(g => g.Count() >= options.MinGroupSize)
            .SelectMany(g => g)
            .Select(p => new ScorePoint((double)p.DevelopmentScore, p.ToTitle, p.PromotionType))
            .ToList();
        return (points, hidden);
    }

    public static IReadOnlyList<StackedValue> PromotionsByTitle(KokpitView data, KokpitOptions options) =>
        (from title in Ordered(data.Promotions.Select(p => p.ToTitle), options.TitleOrder)
         from type in PromotionTypes.All
         select new StackedValue(title, type, data.Promotions.Count(p => p.ToTitle == title && p.PromotionType == type)))
        .ToList();

    private static IEnumerable<string> Ordered(IEnumerable<string> present, string[] order)
    {
        var set = present.ToHashSet();
        return order.Where(set.Contains).Concat(set.Except(order).OrderBy(t => t));
    }
}

/// <summary>Capacity & quality page.</summary>
public static class CapacityCalculator
{
    public static IReadOnlyList<StackedValue> ManDaysByArea(KokpitView data) =>
        data.Capacity.GroupBy(c => c.Area)
            .SelectMany(g => new[]
            {
                new StackedValue(g.Key, "Planlanan", (double)g.Sum(c => c.PlannedManDays)),
                new StackedValue(g.Key, "Gerçekleşen", (double)g.Sum(c => c.ActualManDays))
            })
            .ToList();

    /// <summary>Monthly satisfaction score, weighted by the number of answered surveys.</summary>
    public static IReadOnlyList<MonthlyValue> SurveyTrend(KokpitView data) =>
        data.Capacity.Where(c => c.SurveyScore.HasValue && c.SurveyCount > 0)
            .GroupBy(c => c.Month.Month)
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyValue(g.Key, Formatting.MonthShort(g.Key), "Memnuniyet (1–5)",
                Math.Round((double)(g.Sum(c => c.SurveyScore!.Value * c.SurveyCount) / g.Sum(c => c.SurveyCount)), 2)))
            .ToList();

    private static readonly (string Label, decimal Min, decimal Max)[] TrainingBuckets =
    [
        ("0–19", 0, 20), ("20–29", 20, 30), ("30–39", 30, 40),
        ("40–49", 40, 50), ("50–59", 50, 60), ("60+", 60, decimal.MaxValue)
    ];

    public static IReadOnlyList<CategoryValue> TrainingHistogram(KokpitView data) =>
        TrainingBuckets
            .Select(b => new CategoryValue(b.Label,
                data.Staff.Count(s => s.TrainingHoursYtd >= b.Min && s.TrainingHoursYtd < b.Max), b.Label))
            .ToList();

    public static IReadOnlyList<CategoryValue> Certificates(KokpitView data) =>
        data.Staff.SelectMany(s => s.CertificateList)
            .GroupBy(c => c)
            .Select(g => new CategoryValue(g.Key, g.Count(), g.Key))
            .OrderByDescending(c => c.Value)
            .ToList();

    public static int BelowTrainingTarget(KokpitView data, int targetHours) =>
        data.Staff.Count(s => s.TrainingHoursYtd < targetHours);
}

/// <summary>"Dikkat gerektirenler" list: rule-based deviations collected from every module.</summary>
public static class AttentionCalculator
{
    public static IReadOnlyList<AttentionItem> Build(KokpitView data, KokpitOptions options)
    {
        var items = new List<AttentionItem>();

        var highOverdue = data.Findings
            .Where(f => f.Risk == RiskLevels.High && f.OverdueDays(data.AsOf) > 90)
            .OrderByDescending(f => f.OverdueDays(data.AsOf))
            .ToList();
        items.Add(new AttentionItem(
            "Vadesi 90+ gün geçmiş yüksek riskli bulgu",
            highOverdue.FirstOrDefault() is { } oldest ? $"En eski: {oldest.ActionOwner} · {oldest.OverdueDays(data.AsOf)} gün" : "Yok",
            highOverdue.Count.ToString(), Severity.Critical));

        var overSla = ReportFlowCalculator.LongestWaiting(data, options, take: int.MaxValue)
            .Where(w => w.Status == SlaStatus.Critical).ToList();
        items.Add(new AttentionItem(
            "SLA'yı aşan bekleyen rapor",
            overSla.FirstOrDefault() is { } worst ? $"{worst.ReportId} {worst.AuditName} · Adım {worst.StepCode} · {worst.WaitingDays} gün" : "Yok",
            overSla.Count.ToString(), Severity.Critical));

        var delayed = data.Audits.Where(a => a.IsDelayed && a.Stage != AuditStages.Issued).ToList();
        items.Add(new AttentionItem(
            "Gecikmeli denetim",
            delayed.FirstOrDefault() is { } d ? $"Örn. {d.AuditName} ({d.Stage})" : "Yok",
            delayed.Count.ToString(), Severity.Warning));

        var bottleneck = ReportFlowCalculator.StepStats(data, options)
            .Where(s => s.SampleSize > 0).OrderByDescending(s => s.Ratio).FirstOrDefault();
        if (bottleneck is not null)
        {
            items.Add(new AttentionItem(
                "Rapor akışında darboğaz",
                $"Adım {bottleneck.StepCode}: {bottleneck.StepName} · ort. {Formatting.Number(bottleneck.AverageDays, 1)} g / SLA {bottleneck.SlaDays} g",
                "×" + Formatting.Number(bottleneck.Ratio, 1), Severity.Warning));
        }

        var repeats = data.Findings.Where(f => f.IsRepeat && f.IsOpen).GroupBy(f => f.ActionOwner)
            .OrderByDescending(g => g.Count()).ToList();
        items.Add(new AttentionItem(
            "Tekrarlayan açık bulgu",
            repeats.FirstOrDefault() is { } top ? $"En çok: {top.Key} ({top.Count()})" : "Yok",
            repeats.Sum(g => g.Count()).ToString(), Severity.Serious));

        items.Add(new AttentionItem(
            $"Yıllık eğitim hedefinin ({options.TrainingHoursTarget} saat) altında",
            "Sürekli mesleki gelişim takibi",
            CapacityCalculator.BelowTrainingTarget(data, options.TrainingHoursTarget).ToString(), Severity.Info));

        return items;
    }
}



/// <summary>Turkish number and date formatting used by components and calculators.</summary>
public static class Formatting
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string MonthShort(int month) => Tr.DateTimeFormat.AbbreviatedMonthNames[month - 1];

    public static string Number(double value, int decimals = 0) => value.ToString("N" + decimals, Tr);

    public static string Number(decimal value, int decimals = 0) => value.ToString("N" + decimals, Tr);

    public static string Percent(double ratio) => "%" + (ratio * 100).ToString("N0", Tr);

    public static string Date(DateTime value) => value.ToString("d MMM yyyy", Tr);
}



/// <summary>
/// Chart colours for the Petrol theme. Must stay in sync with wwwroot/css/theme.css.
/// DevExpress charts take System.Drawing.Color, so colours live here as well as in CSS.
/// Categorical order (Blue, Orange, Aqua) is CVD-validated; do not reorder.
/// </summary>
public static class ChartPalette
{
    public static readonly Color Accent = ColorTranslator.FromHtml("#0f6b5c");
    public static readonly Color Ink = ColorTranslator.FromHtml("#12201c");
    public static readonly Color Muted = ColorTranslator.FromHtml("#5d6b66");

    // Categorical (identity)
    public static readonly Color Series1 = ColorTranslator.FromHtml("#2a78d6");
    public static readonly Color Series2 = ColorTranslator.FromHtml("#eb6834");
    public static readonly Color Series3 = ColorTranslator.FromHtml("#1baf7a");

    // Status (reserved: never used for ordinary series)
    public static readonly Color Good = ColorTranslator.FromHtml("#0f8a3c");
    public static readonly Color Warning = ColorTranslator.FromHtml("#b77900");
    public static readonly Color Serious = ColorTranslator.FromHtml("#e0703f");
    public static readonly Color Critical = ColorTranslator.FromHtml("#d03b3b");
    public static readonly Color Low = ColorTranslator.FromHtml("#9aaaa5");

    // Ordinal ramp for audit stages (light = early, dark = late)
    private static readonly Dictionary<string, Color> StageColors = new()
    {
        [AuditStages.NotStarted] = ColorTranslator.FromHtml("#c9d6d2"),
        [AuditStages.Fieldwork] = ColorTranslator.FromHtml("#86c3b5"),
        [AuditStages.Reporting] = ColorTranslator.FromHtml("#2e9a83"),
        [AuditStages.Issued] = ColorTranslator.FromHtml("#0f6b5c")
    };

    // Heatmap ramp
    public static readonly Color HeatLow = ColorTranslator.FromHtml("#e9f3f0");
    public static readonly Color HeatHigh = ColorTranslator.FromHtml("#0f6b5c");

    public static Color Stage(string stage) => StageColors.GetValueOrDefault(stage, Muted);

    public static Color Risk(string risk) => risk switch
    {
        RiskLevels.High => Critical,
        RiskLevels.Medium => Serious,
        _ => Low
    };

    public static Color FindingStatus(string status) => status switch
    {
        FindingStatuses.Open => Critical,
        FindingStatuses.InVerification => Warning,
        _ => Good
    };

    public static Color Promotion(string type) => type == PromotionTypes.Full ? Series1 : Series2;

    public static Color Sla(SlaStatus status) => status switch
    {
        SlaStatus.OnTime => Good,
        SlaStatus.Warning => Warning,
        SlaStatus.Critical => Critical,
        _ => Low
    };

    /// <summary>Unselected points are drawn at ~28% opacity when a filter is active.</summary>
    public static Color Dim(Color color) => Color.FromArgb(72, color);

    public static Color Heat(double intensity)
    {
        var t = Math.Clamp(intensity, 0, 1);
        return Color.FromArgb(
            (int)(HeatLow.R + (HeatHigh.R - HeatLow.R) * t),
            (int)(HeatLow.G + (HeatHigh.G - HeatLow.G) * t),
            (int)(HeatLow.B + (HeatHigh.B - HeatLow.B) * t));
    }

    public static string ToCss(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
}



/// <summary>
/// The only place that reaches into DevExpress chart point objects.
/// Charts bind to small records (Services/Metrics/ChartModels.cs); on click / customize we read the
/// original record back from the point instead of parsing series names or argument strings.
///
/// DX-API: verify ChartSeriesPoint.DataItems and the event argument types against your DevExpress version.
/// If a name differs, fix it here once — chart components do not need to change.
/// </summary>
public static class ChartInterop
{
    public static T? DataItem<T>(ChartSeriesPoint? point) where T : class =>
        point?.DataItems?.OfType<T>().FirstOrDefault();

    public static T? DataItem<T>(ChartSeriesClickEventArgs args) where T : class =>
        DataItem<T>(args.Point);

    public static T? DataItem<T>(ChartSeriesPointCustomizationSettings settings) where T : class =>
        DataItem<T>(settings.Point);
}
