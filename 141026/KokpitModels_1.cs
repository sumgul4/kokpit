namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitModels.cs — data shapes of the cockpit. Property names = column names returned by the SPs
//  (Dapper matches them by name, case-insensitive). New datasets are added here as the data step grows.
// =====================================================================================================

/// <summary>
/// One row of WEB.Kokpit_Actions_SP: an action linked to an audit finding, with its monitoring status.
/// Long free-text columns (finding summary, criteria, action detail, auditor opinion …) are NOT loaded here;
/// they are fetched per action when a detail view needs them.
/// </summary>
public sealed class KokpitAction
{
    // Audit programme / task
    public int ActionId { get; set; }
    public int ProgramYear { get; set; }
    public string? ProgramName { get; set; }
    public string? ProjectDefinition { get; set; }
    public int? TaskId { get; set; }
    public string? TaskName { get; set; }

    // Finding
    public int FindingId { get; set; }
    public string? FindingCode { get; set; }
    public string? FindingName { get; set; }
    public string? FindingStatus { get; set; }
    public string? FindingLevel { get; set; }
    public bool? IsManagementAddressedFinding { get; set; }
    public bool? IsRecurringFinding { get; set; }
    public DateTime? FindingDate { get; set; }

    // Responsible unit
    public int? ResponsibleOrganizationId { get; set; }
    public string? ResponsibleOrganizationName { get; set; }
    public string? Sector { get; set; }
    public int? ResponsibilityPercentage { get; set; }

    // Action
    public string? ActionSummary { get; set; }
    public int? ActionImportanceId { get; set; }
    public string? ActionImportance { get; set; }
    public int? ActionUrgencyId { get; set; }
    public string? ActionUrgency { get; set; }
    public int? ActionConsensusId { get; set; }
    public string? ActionConsensus { get; set; }
    public string? PlannedActionType { get; set; }
    public DateTime? MaxUndertakingDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public int? CompletionRate { get; set; }
    public int? RevisionCount { get; set; }
    public string? ActionResult { get; set; }

    // Calculated in WEB.Kokpit_Actions_SP (single definition shared with the reports)
    /// <summary>CompletionRate = 100 AND ActionResult = 'Tamamlandı'.</summary>
    public bool IsActionCompleted { get; set; }

    /// <summary>Σ (CompletionRate × ResponsibilityPercentage / 100) over all actions of the finding. Same on every row of a finding.</summary>
    public decimal FindingCompletionRate { get; set; }

    /// <summary>Σ ResponsibilityPercentage over the finding's actions. Should be 100; anything else is a data-quality issue.</summary>
    public int FindingResponsibilityTotal { get; set; }

    public int FindingActionCount { get; set; }

    public int FindingCompletedActionCount { get; set; }

    /// <summary>All actions of the finding are completed.</summary>
    public bool IsFindingCompleted { get; set; }

    /// <summary>Not completed and the undertaking date has passed.</summary>
    public bool IsOverdue(DateTime today) =>
        !IsActionCompleted && MaxUndertakingDate is { } due && due.Date < today.Date;

    public int OverdueDays(DateTime today) =>
        IsOverdue(today) ? (today.Date - MaxUndertakingDate!.Value.Date).Days : 0;

    // Classification
    public string? Coso1 { get; set; }
    public string? Coso2 { get; set; }
    public string? Coso3 { get; set; }
    public int? RiskCategoryId { get; set; }
    public string? RiskL1 { get; set; }
    public string? RiskL2 { get; set; }
    public string? RiskL3 { get; set; }
    public string? RiskL4 { get; set; }
    public int? FindingReasonId { get; set; }
    public string? FindingReasonL1 { get; set; }
    public string? FindingReasonL2 { get; set; }

    // Audit trail
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Known code values of the monitoring data, in display order (most severe first).
/// Values are exactly as stored in the table; the cockpit compares them as-is.
/// </summary>
public static class KokpitCodes
{
    public static readonly string[] FindingLevels = ["Yüksek", "Orta", "Düşük", "Gelişim Fırsatı", "Bilgi"];
    public static readonly string[] ActionImportances = ["Yüksek", "Orta", "Düşük"];

    /// <summary>Two scales exist in the data; every value is shown as its own category (no merging).</summary>
    public static readonly string[] ActionUrgencies = ["P1", "P2", "P3", "P4", "Öncelikli", "Ertelenebilir"];
    public static readonly string[] ActionConsensuses = ["Kabul Ediyor", "Kabul Etmiyor"];
    public static readonly string[] PlannedActionTypes =
    [
        "Süreç / Operasyonel",
        "Sistem / Erişim Hakları",
        "Politika / Prosedürler",
        "Risk Yönetimi / Kurumsal Yönetişim",
        "Uyum"
    ];

    /// <summary>Label used in charts for NULL / empty values.</summary>
    public const string Unspecified = "Belirtilmemiş";
}

/// <summary>
/// Everything the cockpit has loaded, shared by all users. Replaced as a whole on refresh, never modified.
/// </summary>
public sealed class KokpitSnapshot
{
    public DateTime LoadedAt { get; init; }
    public IReadOnlyList<KokpitAction> Actions { get; init; } = [];

    /// <summary>Latest ModifiedDate in the data (AY-09 will compare this to decide whether to reload).</summary>
    public DateTime? DataModifiedAt => Actions.Count == 0 ? null : Actions.Max(a => a.ModifiedDate);
}
