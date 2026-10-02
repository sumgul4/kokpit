namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitModels.cs — every type the cockpit uses: codes, dataset rows, chart rows, labels, settings.
// =====================================================================================================

// ---------------------------------------------------------------------------------------------
// CODE VALUES (as they come from the views)
// ---------------------------------------------------------------------------------------------

// Code values as they come from the reporting views. Turkish labels: State/DisplayNames.cs

public static class AuditStages
{
    public const string NotStarted = "Not Started";
    public const string Fieldwork = "Fieldwork";
    public const string Reporting = "Reporting";
    public const string Issued = "Issued";

    public static readonly string[] All = [NotStarted, Fieldwork, Reporting, Issued];
}

public static class RiskLevels
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    public static readonly string[] All = [High, Medium, Low];
}

public static class FindingStatuses
{
    public const string Open = "Open";
    public const string InVerification = "InVerification";
    public const string Closed = "Closed";

    public static readonly string[] All = [Open, InVerification, Closed];
}

public static class AgingBuckets
{
    public const string Days0To30 = "0–30 gün";
    public const string Days31To90 = "31–90 gün";
    public const string Days91To180 = "91–180 gün";
    public const string Over180 = "180+ gün";

    public static readonly string[] All = [Days0To30, Days31To90, Days91To180, Over180];

    public static string For(int ageDays) => ageDays switch
    {
        <= 30 => Days0To30,
        <= 90 => Days31To90,
        <= 180 => Days91To180,
        _ => Over180
    };
}

public static class MovementTypes
{
    public const string Joined = "Joined";
    public const string InternalTransfer = "InternalTransfer";
    public const string Left = "Left";
}

public static class PromotionTypes
{
    public const string Full = "Full";
    public const string Half = "Half";

    public static readonly string[] All = [Full, Half];
}

// ---------------------------------------------------------------------------------------------
// FILTERING CONTRACT
// ---------------------------------------------------------------------------------------------

/// <summary>Every dimension a chart click can filter by. See the filter matrix in the design doc.</summary>
public enum FilterDimension
{
    Area,
    Stage,
    ReportStep,
    Risk,
    FindingStatus,
    ActionOwner,
    AgingBucket,
    Title,
    MovementType,
    MovementDetail,
    PromotionType
}

/// <summary>
/// Implemented by every dataset row that global filters can apply to.
/// Return false when the row does not carry that dimension (the filter then ignores the row).
/// Return true with a null value when the row carries the dimension but has no value
/// (e.g. an issued report has no current step) — such rows are excluded while that filter is active.
/// </summary>
public interface IFilterable
{
    bool TryGetFilterValue(FilterDimension dimension, out string? value);
}

// ---------------------------------------------------------------------------------------------
// DATASET ROWS (one class per reporting view)
// ---------------------------------------------------------------------------------------------

/// <summary>One planned audit. Source view: cockpit.vw_Audit</summary>
public sealed class AuditRecord : IFilterable
{
    public int AuditId { get; init; }
    public string AuditName { get; init; } = "";
    public string Area { get; init; } = "";
    public string Stage { get; init; } = AuditStages.NotStarted;
    public DateTime PlannedStart { get; init; }
    public DateTime PlannedEnd { get; init; }
    public DateTime? PlannedIssueDate { get; init; }
    public DateTime? ActualEnd { get; init; }
    public bool IsDelayed { get; init; }

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        value = dimension switch
        {
            FilterDimension.Area => Area,
            FilterDimension.Stage => Stage,
            _ => null
        };
        return dimension is FilterDimension.Area or FilterDimension.Stage;
    }
}

/// <summary>One audit report and where it currently is in the approval flow. Source view: cockpit.vw_Report</summary>
public sealed class ReportRecord : IFilterable
{
    public string ReportId { get; init; } = "";
    public int AuditId { get; init; }
    public string AuditName { get; init; } = "";
    public string Area { get; init; } = "";
    public string AuditStage { get; init; } = AuditStages.Reporting;

    /// <summary>Null when the report is issued (all steps completed).</summary>
    public string? CurrentStepCode { get; init; }
    public DateTime? CurrentStepEnteredAt { get; init; }

    public DateTime? IssuedAt { get; init; }
    public int? CycleDays { get; init; }

    /// <summary>True when the report was uploaded directly to a later step (first steps skipped).</summary>
    public bool HasSkippedSteps { get; init; }

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        switch (dimension)
        {
            case FilterDimension.Area: value = Area; return true;
            case FilterDimension.Stage: value = AuditStage; return true;
            case FilterDimension.ReportStep: value = CurrentStepCode; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>
/// One completed or in-progress step of one report. Source view: cockpit.vw_ReportStep
/// Not filtered directly: KokpitView keeps the steps of the reports that pass the filters.
/// </summary>
public sealed class ReportStepRecord
{
    public string ReportId { get; init; } = "";
    public string StepCode { get; init; } = "";
    public DateTime EnteredAt { get; init; }
    public DateTime? ExitedAt { get; init; }

    /// <summary>Calendar or business days, as defined by the data team. Null while the step is still open.</summary>
    public decimal? WaitDays { get; init; }
}

/// <summary>Report approval step definition (1–6, A, B, 7–12) and its SLA. Source view: cockpit.vw_StepSla</summary>
public sealed class StepSlaRecord
{
    public string StepCode { get; init; } = "";
    public string StepName { get; init; } = "";
    public int SlaDays { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>One audit finding and its action. Source view: cockpit.vw_Finding</summary>
public sealed class FindingRecord : IFilterable
{
    public int FindingId { get; init; }
    public int? AuditId { get; init; }
    public string Area { get; init; } = "";
    public string Risk { get; init; } = RiskLevels.Medium;
    public string ActionOwner { get; init; } = "";
    public DateTime ReportDate { get; init; }
    public DateTime DueDate { get; init; }
    public DateTime? ClosedDate { get; init; }
    public string Status { get; init; } = FindingStatuses.Open;
    public bool IsRepeat { get; init; }

    /// <summary>Set by KokpitDataService after loading (depends on the snapshot date). Null for closed findings.</summary>
    public string? AgingBucket { get; internal set; }

    public bool IsOpen => Status != FindingStatuses.Closed;

    public int OverdueDays(DateOnly asOf)
    {
        if (!IsOpen) return 0;
        var days = asOf.DayNumber - DateOnly.FromDateTime(DueDate).DayNumber;
        return Math.Max(0, days);
    }

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        switch (dimension)
        {
            case FilterDimension.Area: value = Area; return true;
            case FilterDimension.Risk: value = Risk; return true;
            case FilterDimension.FindingStatus: value = Status; return true;
            case FilterDimension.ActionOwner: value = ActionOwner; return true;
            case FilterDimension.AgingBucket: value = AgingBucket; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>One active staff member of the audit board. Source view: cockpit.vw_Staff</summary>
public sealed class StaffRecord : IFilterable
{
    public string EmployeeId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Area { get; init; } = "";
    public DateTime HireDate { get; init; }
    public decimal TrainingHoursYtd { get; init; }

    /// <summary>Comma separated, e.g. "CIA,CISA". Empty when none.</summary>
    public string Certificates { get; init; } = "";

    public IEnumerable<string> CertificateList =>
        Certificates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        switch (dimension)
        {
            case FilterDimension.Area: value = Area; return true;
            case FilterDimension.Title: value = Title; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>One join, internal transfer or leave in the current year. Source view: cockpit.vw_StaffMovement</summary>
public sealed class StaffMovementRecord : IFilterable
{
    public string EmployeeId { get; init; } = "";
    public DateTime MovementDate { get; init; }
    public string MovementType { get; init; } = MovementTypes.Joined;

    /// <summary>Source of a join (exam, transfer) or target of a leave (branch, unit, resignation, retirement).</summary>
    public string MovementDetail { get; init; } = "";
    public string Title { get; init; } = "";
    public string Area { get; init; } = "";

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        switch (dimension)
        {
            case FilterDimension.Area: value = Area; return true;
            case FilterDimension.Title: value = Title; return true;
            case FilterDimension.MovementType: value = MovementType; return true;
            case FilterDimension.MovementDetail: value = MovementDetail; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>One promotion decision in the current period. Source view: cockpit.vw_Promotion</summary>
public sealed class PromotionRecord : IFilterable
{
    public string EmployeeId { get; init; } = "";
    public int PeriodYear { get; init; }
    public string FromTitle { get; init; } = "";
    public string ToTitle { get; init; } = "";
    public decimal DevelopmentScore { get; init; }
    public string PromotionType { get; init; } = PromotionTypes.Full;
    public string Area { get; init; } = "";

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        switch (dimension)
        {
            case FilterDimension.Area: value = Area; return true;
            case FilterDimension.Title: value = ToTitle; return true;
            case FilterDimension.PromotionType: value = PromotionType; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>Planned vs actual man-days and survey score for one area in one month. Source view: cockpit.vw_CapacityMonthly</summary>
public sealed class CapacityRecord : IFilterable
{
    public string Area { get; init; } = "";
    public DateTime Month { get; init; }
    public decimal PlannedManDays { get; init; }
    public decimal ActualManDays { get; init; }

    /// <summary>Average auditee satisfaction score (1–5). Null when no survey was answered that month.</summary>
    public decimal? SurveyScore { get; init; }
    public int SurveyCount { get; init; }

    public bool TryGetFilterValue(FilterDimension dimension, out string? value)
    {
        value = dimension == FilterDimension.Area ? Area : null;
        return dimension == FilterDimension.Area;
    }
}

// ---------------------------------------------------------------------------------------------
// CHART-READY ROWS (produced by KokpitMetrics, bound to DevExpress charts)
// ---------------------------------------------------------------------------------------------

// Small, chart-ready rows produced by the calculators and bound directly to DevExpress charts.
// Field names are what the charts' ArgumentField / ValueField lambdas use.

/// <summary>One category and its value (bar, donut).</summary>
public sealed record CategoryValue(string Category, double Value, string? FilterValue = null);

/// <summary>One segment of a stacked bar: Argument on the axis, Series = stack segment.</summary>
public sealed record StackedValue(string Argument, string Series, double Value);

/// <summary>One point of a monthly line; Value is null for months that have not happened yet.</summary>
public sealed record MonthlyValue(int Month, string MonthLabel, string Series, double? Value);

public enum SlaStatus { NoData, OnTime, Warning, Critical }

public sealed record StepStat(
    string StepCode,
    string StepName,
    int SlaDays,
    int WaitingCount,
    double AverageDays,
    int SampleSize,
    SlaStatus Status)
{
    public double Ratio => SlaDays == 0 ? 0 : AverageDays / SlaDays;
}

public sealed record WaitingReport(string ReportId, string AuditName, string StepCode, int WaitingDays, int SlaDays, SlaStatus Status);

public sealed record HeatmapCell(string Row, string Column, int Count, double Intensity);

/// <summary>Waterfall bar drawn as a stacked bar: an invisible Base segment plus the visible Value segment.</summary>
public sealed record WaterfallStep(string Label, double Base, double Value, string DisplayText, string? FilterValue);

public sealed record FlowLink(string Source, string Target, int Weight);

public sealed record ScorePoint(double Score, string Title, string Series);

public sealed record GanttItem(int AuditId, string Label, DateTime Start, DateTime End, string Stage, bool IsDelayed);

public enum Severity { Info, Warning, Serious, Critical }

public sealed record AttentionItem(string Title, string Detail, string Value, Severity Severity);

public sealed record KpiSummary(
    double PlanCompletionRatio,
    int IssuedDueCount,
    int DueCount,
    int OngoingAudits,
    int DelayedAudits,
    int OpenFindings,
    int OpenHighRiskFindings,
    int OverdueActions,
    int OverdueOver90,
    double? AverageCycleDays,
    int CycleTargetDays,
    int Headcount,
    int Joined,
    int Left);

// ---------------------------------------------------------------------------------------------
// TURKISH UI LABELS
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Turkish labels for the UI. Code and database values stay in English;
/// only this class knows how they are shown to the user.
/// </summary>
public static class DisplayNames
{
    public static string Dimension(FilterDimension dimension) => dimension switch
    {
        FilterDimension.Area => "Denetim alanı",
        FilterDimension.Stage => "Aşama",
        FilterDimension.ReportStep => "Rapor adımı",
        FilterDimension.Risk => "Risk",
        FilterDimension.FindingStatus => "Bulgu durumu",
        FilterDimension.ActionOwner => "Aksiyon sahibi",
        FilterDimension.AgingBucket => "Yaşlandırma",
        FilterDimension.Title => "Unvan",
        FilterDimension.MovementType => "Hareket",
        FilterDimension.MovementDetail => "Hareket detayı",
        FilterDimension.PromotionType => "Terfi türü",
        _ => dimension.ToString()
    };

    public static string Value(FilterDimension dimension, string value) => dimension switch
    {
        FilterDimension.Risk => Risk(value),
        FilterDimension.FindingStatus => FindingStatus(value),
        FilterDimension.MovementType => MovementType(value),
        FilterDimension.PromotionType => PromotionType(value),
        _ => value
    };

    public static string Risk(string value) => value switch
    {
        RiskLevels.High => "Yüksek",
        RiskLevels.Medium => "Orta",
        RiskLevels.Low => "Düşük",
        _ => value
    };

    public static string FindingStatus(string value) => value switch
    {
        FindingStatuses.Open => "Açık",
        FindingStatuses.InVerification => "Doğrulamada",
        FindingStatuses.Closed => "Kapalı",
        _ => value
    };

    public static string MovementType(string value) => value switch
    {
        MovementTypes.Joined => "Yeni katılım",
        MovementTypes.InternalTransfer => "İç rotasyon",
        MovementTypes.Left => "Ayrılış",
        _ => value
    };

    public static string PromotionType(string value) => value switch
    {
        PromotionTypes.Full => "Tam",
        PromotionTypes.Half => "Yarım",
        _ => value
    };
}

// ---------------------------------------------------------------------------------------------
// SETTINGS (appsettings.json → "Kokpit" section)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Dashboard-wide settings bound from appsettings.json ("Dashboard" section).
/// Business targets live here so they can change without a deploy.
/// </summary>
public sealed class KokpitOptions
{
    public const string SectionName = "Kokpit";

    /// <summary>How often the background worker checks the database for changes.</summary>
    public int RefreshIntervalSeconds { get; set; } = 120;

    /// <summary>If the last successful load is older than this, the UI shows a stale-data warning.</summary>
    public int StaleAfterMinutes { get; set; } = 15;

    public int CycleTimeTargetDays { get; set; } = 60;

    public int TrainingHoursTarget { get; set; } = 40;

    /// <summary>KVKK: HR groups smaller than this are hidden or aggregated.</summary>
    public int MinGroupSize { get; set; } = 5;

    /// <summary>Wait/SLA ratio above which a step is shown as critical (between 1 and this value = warning).</summary>
    public double SlaWarningRatio { get; set; } = 1.25;

    /// <summary>Titles from junior to senior; used to order the title charts.</summary>
    public string[] TitleOrder { get; set; } =
    [
        "Müfettiş Yardımcısı",
        "Yetkili Müfettiş Yardımcısı",
        "Müfettiş",
        "Kıdemli Müfettiş",
        "Başmüfettiş",
        "Yönetici"
    ];

    /// <summary>Name of the audit board as a node in the internal mobility (Sankey) chart.</summary>
    public string BoardName { get; set; } = "Teftiş Kurulu";

    /// <summary>AD groups that may open the cockpit. Empty = any signed-in user.</summary>
    public string[] ViewerRoles { get; set; } = [];

    /// <summary>AD groups that may open the HR page. Empty = any signed-in user.</summary>
    public string[] HrRoles { get; set; } = [];
}

public static class KokpitPolicies
{
    public const string Viewer = "KokpitViewer";
    public const string HrViewer = "KokpitHrViewer";
}
