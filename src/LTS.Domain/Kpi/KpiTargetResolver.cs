using LTS.Domain.Entities;
using LTS.Domain.Enums;

namespace LTS.Domain.Kpi;

/// <summary>Identifies which KPI targets apply to a shipment.</summary>
/// <param name="ExportTypeId">The shipment's export type.</param>
/// <param name="LoadingCountryCode">Country of the shipment's loading point.</param>
/// <param name="ArrivalCountryId">The receiving country.</param>
/// <param name="OnDate">
/// The date the targets are read as of — the loading date, so revising a KPI does not
/// retroactively re-score shipments that already moved.
/// </param>
public readonly record struct KpiLookupKey(
    int? ExportTypeId,
    string? LoadingCountryCode,
    int ArrivalCountryId,
    DateOnly OnDate);

/// <summary>
/// Matches shipments to KPI targets. Built once from the full target list and reused across a
/// page of shipments, so a grid costs one query rather than one per row.
/// </summary>
public sealed class KpiTargetResolver
{
    private readonly Dictionary<KpiStep, List<KpiTarget>> _byStep;

    public KpiTargetResolver(IEnumerable<KpiTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        _byStep = targets
            .GroupBy(t => t.Step)
            // Most specific first, then newest, so the first match found is the winner.
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.Specificity)
                      .ThenByDescending(t => t.EffectiveFrom)
                      .ToList());
    }

    public static KpiTargetResolver Empty { get; } = new([]);

    /// <summary>The winning target row for a step, or <c>null</c> when nothing matches.</summary>
    public KpiTarget? Match(KpiStep step, KpiLookupKey key)
    {
        if (!_byStep.TryGetValue(step, out var candidates))
        {
            return null;
        }

        foreach (var target in candidates)
        {
            if (Matches(target, key))
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>Target duration in days for a step, or <c>null</c> when no target matches.</summary>
    public int? TargetDays(KpiStep step, KpiLookupKey key) => Match(step, key)?.TargetDays;

    /// <summary>A resolver function bound to one shipment, ready to hand to <see cref="KpiEvaluator"/>.</summary>
    public Func<KpiStep, int?> For(KpiLookupKey key) => step => TargetDays(step, key);

    private static bool Matches(KpiTarget target, KpiLookupKey key)
    {
        if (!target.IsEffectiveOn(key.OnDate))
        {
            return false;
        }

        // A null key column on the target means "any", so it only has to match when it is set.
        if (target.ExportTypeId is { } exportTypeId && exportTypeId != key.ExportTypeId)
        {
            return false;
        }

        if (target.LoadingCountryCode is { } loadingCountry &&
            !string.Equals(loadingCountry, key.LoadingCountryCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return target.ArrivalCountryId is not { } arrivalCountryId || arrivalCountryId == key.ArrivalCountryId;
    }
}
