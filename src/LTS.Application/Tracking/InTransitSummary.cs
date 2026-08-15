using LTS.Domain.Enums;

namespace LTS.Application.Tracking;

/// <summary>
/// The Shipments On The Way dashboard: everything still short of a store arrival, cut the ways
/// a logistics desk actually asks about it — where is it, is it late, and whose is it.
/// </summary>
public sealed record InTransitSummary
{
    public static readonly InTransitSummary Empty = new()
    {
        ByStatus = [],
        ByPerformance = [],
        Aging = [],
        ByLogisticsCompany = [],
        ByBroker = []
    };

    public int ShipmentCount { get; init; }
    public int TransferCount { get; init; }
    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }

    /// <summary>Shipments whose current step has already passed its KPI target.</summary>
    public int OverdueCount { get; init; }

    /// <summary>Shipments inside target but close enough to it to warrant a nudge.</summary>
    public int AtRiskCount { get; init; }

    public required IReadOnlyList<CountBucket<TrackingStatus>> ByStatus { get; init; }
    public required IReadOnlyList<CountBucket<PerformanceStatus>> ByPerformance { get; init; }

    /// <summary>How long shipments have been in flight, bucketed by days since loading.</summary>
    public required IReadOnlyList<CountBucket<string>> Aging { get; init; }

    public required IReadOnlyList<PartnerBucket> ByLogisticsCompany { get; init; }
    public required IReadOnlyList<PartnerBucket> ByBroker { get; init; }
}

/// <summary>A labelled count for one dashboard segment.</summary>
public sealed record CountBucket<TKey>(TKey Key, string Label, int Count);

/// <summary>In-flight volume for one partner, with how much of it is running late.</summary>
public sealed record PartnerBucket(string Name, int Count, int OverdueCount)
{
    public double OverdueShare => Count == 0 ? 0 : (double)OverdueCount / Count;
}
