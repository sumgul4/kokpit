namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitFilterState.cs — cross-filtering.
//    KokpitFilterState  the user's selections (Scoped = one per browser tab), raises Changed
//    KokpitView         the shared snapshot with those selections applied; input of every metric
// =====================================================================================================

/// <summary>
/// Global filter selections for ONE user session (registered as Scoped = one per Blazor circuit).
/// Survives tab navigation. Charts call Toggle/TogglePair; pages listen to <see cref="Changed"/>.
/// Values inside one dimension combine with OR, different dimensions combine with AND.
/// </summary>
public sealed class KokpitFilterState
{
    private readonly Dictionary<FilterDimension, HashSet<string>> _selections = new();

    public event Action? Changed;

    /// <summary>When on, a click adds to the current selection of that dimension instead of replacing it.</summary>
    public bool MultiSelect { get; private set; }

    public bool HasAny => _selections.Count > 0;

    public IReadOnlyDictionary<FilterDimension, IReadOnlySet<string>> Selections =>
        _selections.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);

    public bool IsActive(FilterDimension dimension) => _selections.ContainsKey(dimension);

    /// <summary>True when the value is not filtered out. Charts use this to dim unselected points.</summary>
    public bool IsVisible(FilterDimension dimension, string? value) =>
        !_selections.TryGetValue(dimension, out var set) || (value is not null && set.Contains(value));

    public void Toggle(FilterDimension dimension, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (_selections.TryGetValue(dimension, out var set) && set.Contains(value))
        {
            set.Remove(value);
            if (set.Count == 0) _selections.Remove(dimension);
        }
        else
        {
            if (!_selections.TryGetValue(dimension, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _selections[dimension] = set;
            }
            if (!MultiSelect) set.Clear();
            set.Add(value);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Selects two dimensions at once (e.g. heatmap cell = owner + risk, stacked bar = area + stage).
    /// Clicking the same pair again clears both.
    /// </summary>
    public void TogglePair(FilterDimension first, string? firstValue, FilterDimension second, string? secondValue)
    {
        if (string.IsNullOrEmpty(firstValue) || string.IsNullOrEmpty(secondValue)) return;

        if (MultiSelect)
        {
            Toggle(second, secondValue);
            return;
        }

        var alreadySelected = IsOnly(first, firstValue) && IsOnly(second, secondValue);
        _selections.Remove(first);
        _selections.Remove(second);
        if (!alreadySelected)
        {
            _selections[first] = new HashSet<string>(StringComparer.Ordinal) { firstValue };
            _selections[second] = new HashSet<string>(StringComparer.Ordinal) { secondValue };
        }
        Changed?.Invoke();
    }

    public void Remove(FilterDimension dimension, string value)
    {
        if (!_selections.TryGetValue(dimension, out var set) || !set.Remove(value)) return;
        if (set.Count == 0) _selections.Remove(dimension);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_selections.Count == 0) return;
        _selections.Clear();
        Changed?.Invoke();
    }

    public void SetMultiSelect(bool enabled) => MultiSelect = enabled;

    public bool Matches(IFilterable item)
    {
        foreach (var (dimension, values) in _selections)
        {
            if (!item.TryGetFilterValue(dimension, out var value)) continue;
            if (value is null || !values.Contains(value)) return false;
        }
        return true;
    }

    public IReadOnlyList<T> Apply<T>(IReadOnlyList<T> items) where T : IFilterable =>
        _selections.Count == 0 ? items : items.Where(item => Matches(item)).ToList();

    private bool IsOnly(FilterDimension dimension, string value) =>
        _selections.TryGetValue(dimension, out var set) && set.Count == 1 && set.Contains(value);
}

/// <summary>
/// The shared snapshot with the current user's filters applied. Built in memory (milliseconds),
/// rebuilt whenever the filters or the snapshot change. Every calculator takes this as input.
/// </summary>
public sealed class KokpitView
{
    public static KokpitView Empty { get; } = new() { Snapshot = KokpitSnapshot.Empty };

    public required KokpitSnapshot Snapshot { get; init; }

    public DateOnly AsOf => Snapshot.AsOf;
    public IReadOnlyList<StepSlaRecord> Steps => Snapshot.Steps;

    public IReadOnlyList<AuditRecord> Audits { get; init; } = [];
    public IReadOnlyList<ReportRecord> Reports { get; init; } = [];
    public IReadOnlyList<ReportStepRecord> ReportSteps { get; init; } = [];
    public IReadOnlyList<FindingRecord> Findings { get; init; } = [];
    public IReadOnlyList<StaffRecord> Staff { get; init; } = [];
    public IReadOnlyList<StaffMovementRecord> Movements { get; init; } = [];
    public IReadOnlyList<PromotionRecord> Promotions { get; init; } = [];
    public IReadOnlyList<CapacityRecord> Capacity { get; init; } = [];

    public static KokpitView Create(KokpitSnapshot snapshot, KokpitFilterState filters)
    {
        var reports = filters.Apply(snapshot.Reports);
        var reportIds = reports.Select(r => r.ReportId).ToHashSet(StringComparer.Ordinal);

        return new KokpitView
        {
            Snapshot = snapshot,
            Audits = filters.Apply(snapshot.Audits),
            Reports = reports,
            // Step history follows its report: keep only the steps of the reports that passed the filters.
            ReportSteps = filters.HasAny
                ? snapshot.ReportSteps.Where(s => reportIds.Contains(s.ReportId)).ToList()
                : snapshot.ReportSteps,
            Findings = filters.Apply(snapshot.Findings),
            Staff = filters.Apply(snapshot.Staff),
            Movements = filters.Apply(snapshot.Movements),
            Promotions = filters.Apply(snapshot.Promotions),
            Capacity = filters.Apply(snapshot.Capacity)
        };
    }
}
