namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitFilters.cs — cross-filtering for the whole cockpit.
//
//  - ONE KokpitFilterState per open page (created in Kokpit.razor, shared with every tab by CascadingValue).
//  - A click on a chart bar / heatmap cell calls Toggle(dimension, value); every card re-calculates from
//    Apply(actions) and the filter bar shows each active value as a chip.
//  - Different dimensions are combined with AND. Within one dimension: single select replaces the value,
//    multi select (checkbox in the filter bar) adds it → values of the same dimension are combined with OR.
//  - Filters work on action rows; finding counts are taken from the filtered actions
//    ("Sektör = X" on findings means: the finding has at least one action in sector X).
// =====================================================================================================

public enum KokpitDimension
{
    Sector,
    FindingLevel,
    ResponsibleOrganization,
    OverdueBucket,
    DueWindow,
    ActionUrgency,
    ActionImportance
}

public sealed class KokpitFilterState
{
    private readonly Dictionary<KokpitDimension, HashSet<string>> _selected = new();

    /// <summary>Raised after any change; components re-render on it.</summary>
    public event Action? Changed;

    /// <summary>False: a click replaces the dimension's value. True: a click adds/removes it.</summary>
    public bool MultiSelect { get; set; }

    public bool HasAny => _selected.Count > 0;

    /// <summary>Active filters in the order they were added, for the chip list.</summary>
    public IEnumerable<(KokpitDimension Dimension, string Value)> Active =>
        _selected.SelectMany(p => p.Value.Select(v => (p.Key, v)));

    public bool IsSelected(KokpitDimension dimension, string value) =>
        _selected.TryGetValue(dimension, out var values) && values.Contains(value);

    public void Toggle(KokpitDimension dimension, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        ToggleCore(dimension, value);
        Changed?.Invoke();
    }

    /// <summary>Two dimensions at once (heatmap cell, stacked bar segment) — one notification.</summary>
    public void Toggle(KokpitDimension first, string? firstValue, KokpitDimension second, string? secondValue)
    {
        if (string.IsNullOrEmpty(firstValue) || string.IsNullOrEmpty(secondValue)) return;

        // Clicking the same cell again clears it; otherwise both values are set.
        if (IsSelected(first, firstValue) && IsSelected(second, secondValue))
        {
            RemoveCore(first, firstValue);
            RemoveCore(second, secondValue);
        }
        else
        {
            if (!IsSelected(first, firstValue)) ToggleCore(first, firstValue);
            if (!IsSelected(second, secondValue)) ToggleCore(second, secondValue);
        }
        Changed?.Invoke();
    }

    public void Remove(KokpitDimension dimension, string value)
    {
        RemoveCore(dimension, value);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Changed?.Invoke();
    }

    public void SetMultiSelect(bool value)
    {
        MultiSelect = value;
        Changed?.Invoke();
    }

    /// <summary>
    /// The rows that satisfy every active filter, except the dimensions listed in <paramref name="ignore"/>.
    /// A chart ignores its OWN dimensions: its other bars stay visible (so more values can be picked in
    /// multi-select) and the selection is shown as highlight / badge instead.
    /// </summary>
    public IReadOnlyList<KokpitAction> Apply(IEnumerable<KokpitAction> actions, DateTime today, params KokpitDimension[] ignore)
    {
        var active = _selected.Where(p => !ignore.Contains(p.Key)).ToList();
        if (active.Count == 0) return actions as IReadOnlyList<KokpitAction> ?? actions.ToList();

        return actions.Where(a => active.All(p =>
            KokpitDimensions.ValueOf(a, p.Key, today) is { } value && p.Value.Contains(value))).ToList();
    }

    /// <summary>Selected values of the given dimensions, for the "Seçili: …" badge on a chart card.</summary>
    public IReadOnlyList<string> SelectedValues(params KokpitDimension[] dimensions) =>
        dimensions.Where(_selected.ContainsKey).SelectMany(d => _selected[d]).ToList();

    private void ToggleCore(KokpitDimension dimension, string value)
    {
        if (!_selected.TryGetValue(dimension, out var values))
        {
            _selected[dimension] = [value];
            return;
        }

        if (values.Remove(value))
        {
            if (values.Count == 0) _selected.Remove(dimension);
            return;
        }

        if (!MultiSelect) values.Clear();
        values.Add(value);
    }

    private void RemoveCore(KokpitDimension dimension, string value)
    {
        if (_selected.TryGetValue(dimension, out var values) && values.Remove(value) && values.Count == 0)
            _selected.Remove(dimension);
    }
}

/// <summary>How each filter dimension reads its value from an action row. Charts use the same labels.</summary>
public static class KokpitDimensions
{
    public static readonly string[] OverdueBuckets = ["0–30 gün", "31–90 gün", "91–180 gün", "180+ gün"];
    public static readonly string[] DueWindows = ["0–30 gün", "31–60 gün", "61–90 gün"];

    public static string Label(KokpitDimension dimension) => dimension switch
    {
        KokpitDimension.Sector => "Sektör",
        KokpitDimension.FindingLevel => "Bulgu seviyesi",
        KokpitDimension.ResponsibleOrganization => "Sorumlu birim",
        KokpitDimension.OverdueBucket => "Gecikme",
        KokpitDimension.DueWindow => "Vadesine kalan",
        KokpitDimension.ActionUrgency => "Aciliyet",
        KokpitDimension.ActionImportance => "Önem",
        _ => dimension.ToString()
    };

    /// <summary>
    /// The row's value for a dimension. NULL/empty text becomes "Belirtilmemiş" (so it can be clicked too).
    /// Returns null when the dimension does not apply to the row (e.g. gecikme for an action that is not overdue):
    /// such rows drop out while that filter is active.
    /// </summary>
    public static string? ValueOf(KokpitAction a, KokpitDimension dimension, DateTime today) => dimension switch
    {
        KokpitDimension.Sector => Text(a.Sector),
        KokpitDimension.FindingLevel => Text(a.FindingLevel),
        KokpitDimension.ResponsibleOrganization => Text(a.ResponsibleOrganizationName),
        KokpitDimension.OverdueBucket => OverdueBucket(a, today),
        KokpitDimension.DueWindow => DueWindow(a, today),
        KokpitDimension.ActionUrgency => Text(a.ActionUrgency),
        KokpitDimension.ActionImportance => Text(a.ActionImportance),
        _ => null
    };

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? KokpitCodes.Unspecified : value.Trim();

    public static string? OverdueBucket(KokpitAction a, DateTime today)
    {
        if (!a.IsOverdue(today)) return null;
        var days = a.OverdueDays(today);
        return days <= 30 ? OverdueBuckets[0] : days <= 90 ? OverdueBuckets[1] : days <= 180 ? OverdueBuckets[2] : OverdueBuckets[3];
    }

    /// <summary>Open actions whose undertaking date is today or within the next 90 days.</summary>
    public static string? DueWindow(KokpitAction a, DateTime today)
    {
        if (a.IsActionCompleted || a.MaxUndertakingDate is not { } due || due.Date < today.Date) return null;
        var days = (due.Date - today.Date).Days;
        return days <= 30 ? DueWindows[0] : days <= 60 ? DueWindows[1] : days <= 90 ? DueWindows[2] : null;
    }
}
