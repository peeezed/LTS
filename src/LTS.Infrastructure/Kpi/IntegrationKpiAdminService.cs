using LTS.Application.Kpi;
using LTS.Domain.Kpi;
using LTS.Infrastructure.Persistence;
using LTS.Infrastructure.Reference;
using Microsoft.EntityFrameworkCore;

namespace LTS.Infrastructure.Kpi;

/// <summary>
/// CRUD over LTS_KpiTargets, always scoped to one country. No Excel round-trip in this round.
/// countryId parameters are the app-wide offset id (see IIntegrationKpiAdminService); converted to
/// LTS_Integration's own raw id before touching LTS_KpiTargets.CountryId.
/// </summary>
public sealed class IntegrationKpiAdminService(IDbContextFactory<LtsIntegrationDbContext> dbFactory) : IIntegrationKpiAdminService
{
    public async Task<IReadOnlyList<IntegrationKpiTargetRow>> GetTargetsAsync(
        int countryId, CancellationToken cancellationToken = default)
    {
        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.KpiTargets.AsNoTracking()
            .Where(t => t.CountryId == rawCountryId)
            .ToListAsync(cancellationToken);

        return [.. rows.OrderBy(t => t.Step).ThenByDescending(t => Specificity(t)).Select(Map)];
    }

    public async Task<int> SaveAsync(
        int countryId, IntegrationKpiTargetInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.TargetDays < 0)
        {
            throw new ArgumentException("A KPI target cannot be negative.", nameof(input));
        }

        var rawCountryId = IntegrationCountryId.ToRawId(countryId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var target = input.Id is { } id
            ? await db.KpiTargets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"KPI target {id} does not exist.")
            : new LtsIntegrationKpiTarget { CountryId = rawCountryId, Step = input.Step, TargetDays = input.TargetDays };

        if (input.Id is null)
        {
            db.KpiTargets.Add(target);
        }

        target.CountryId = rawCountryId;
        target.Step = input.Step;
        target.ExportType = Trim(input.ExportType);
        target.LoadingPoint = Trim(input.LoadingPoint);
        target.ArrivalCustoms = Trim(input.ArrivalCustoms);
        target.TransportType = Trim(input.TransportType);
        target.TargetDays = input.TargetDays;
        target.IsActive = input.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return target.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var target = await db.KpiTargets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null)
        {
            return;
        }

        db.KpiTargets.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int Specificity(LtsIntegrationKpiTarget target) => IntegrationKpiResolver.Specificity(
        new KpiAttributeScope(target.ExportType, target.LoadingPoint, target.ArrivalCustoms, target.TransportType));

    private static IntegrationKpiTargetRow Map(LtsIntegrationKpiTarget target) => new()
    {
        Id = target.Id,
        Step = target.Step,
        StepName = IntegrationKpiCatalog.Get(target.Step).DisplayName,
        ExportType = target.ExportType,
        LoadingPoint = target.LoadingPoint,
        ArrivalCustoms = target.ArrivalCustoms,
        TransportType = target.TransportType,
        TargetDays = target.TargetDays,
        IsActive = target.IsActive,
        Specificity = Specificity(target)
    };
}
