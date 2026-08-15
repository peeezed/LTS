using FluentAssertions;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;

namespace LTS.Tests.Kpi;

public class KpiTargetResolverTests
{
    private const int Definitive = 1;
    private const int Transit = 2;
    private const int Turkey = 10;
    private const int Germany = 20;

    private static readonly DateOnly Loaded = new(2026, 3, 1);

    private static KpiTarget Target(
        int days,
        int? exportTypeId = null,
        string? loadingCountry = null,
        int? arrivalCountryId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        bool isActive = true) => new()
        {
            Step = KpiStep.DepartureToArrival,
            TargetDays = days,
            ExportTypeId = exportTypeId,
            LoadingCountryCode = loadingCountry,
            ArrivalCountryId = arrivalCountryId,
            EffectiveFrom = from ?? new DateOnly(2020, 1, 1),
            EffectiveTo = to,
            IsActive = isActive
        };

    private static KpiLookupKey Key(int? exportTypeId = Definitive, string? loadingCountry = "DE", int arrivalCountryId = Turkey) =>
        new(exportTypeId, loadingCountry, arrivalCountryId, Loaded);

    [Fact]
    public void Most_specific_matching_row_wins_over_wildcards()
    {
        var resolver = new KpiTargetResolver(
        [
            Target(10),
            Target(7, arrivalCountryId: Turkey),
            Target(4, exportTypeId: Definitive, loadingCountry: "DE", arrivalCountryId: Turkey),
            Target(6, exportTypeId: Definitive, arrivalCountryId: Turkey)
        ]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key()).Should().Be(4);
    }

    [Fact]
    public void Falls_back_to_the_global_row_when_nothing_specific_matches()
    {
        var resolver = new KpiTargetResolver(
        [
            Target(10),
            Target(4, exportTypeId: Definitive, loadingCountry: "DE", arrivalCountryId: Turkey)
        ]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key(loadingCountry: "IT")).Should().Be(10);
    }

    [Fact]
    public void Returns_null_when_no_row_matches_at_all()
    {
        var resolver = new KpiTargetResolver([Target(4, arrivalCountryId: Germany)]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key()).Should().BeNull();
    }

    [Fact]
    public void Loading_country_matching_ignores_case()
    {
        var resolver = new KpiTargetResolver([Target(4, loadingCountry: "de")]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key(loadingCountry: "DE")).Should().Be(4);
    }

    [Fact]
    public void Targets_are_read_as_of_the_shipment_date_so_revisions_do_not_rewrite_history()
    {
        var resolver = new KpiTargetResolver(
        [
            Target(5, to: new DateOnly(2026, 2, 28)),
            Target(3, from: new DateOnly(2026, 3, 1))
        ]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key() with { OnDate = new DateOnly(2026, 1, 15) })
            .Should().Be(5);
        resolver.TargetDays(KpiStep.DepartureToArrival, Key()).Should().Be(3);
    }

    [Fact]
    public void Inactive_rows_are_ignored()
    {
        var resolver = new KpiTargetResolver(
        [
            Target(4, exportTypeId: Definitive, loadingCountry: "DE", arrivalCountryId: Turkey, isActive: false),
            Target(9)
        ]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key()).Should().Be(9);
    }

    [Fact]
    public void A_shipment_with_no_export_type_only_matches_wildcard_rows()
    {
        var resolver = new KpiTargetResolver(
        [
            Target(4, exportTypeId: Transit),
            Target(9)
        ]);

        resolver.TargetDays(KpiStep.DepartureToArrival, Key(exportTypeId: null)).Should().Be(9);
    }

    [Fact]
    public void Steps_without_targets_resolve_to_null_independently()
    {
        var resolver = new KpiTargetResolver([Target(4)]);
        var key = Key();

        resolver.TargetDays(KpiStep.DepartureToArrival, key).Should().Be(4);
        resolver.TargetDays(KpiStep.CustomsStartToCustomsEnd, key).Should().BeNull();
    }
}
