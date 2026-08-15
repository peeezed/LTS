using LTS.Application.Security;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Domain.Security;
using LTS.Domain.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LTS.Infrastructure.Persistence;

/// <summary>
/// Builds a realistic demo dataset: two countries, their reference data, KPI targets, users of
/// each type, a mock integration source, and shipments spread across every stage of the
/// lifecycle. Without it there is nothing to look at until a real integration is connected,
/// and no way to check that statuses, KPI scoring and permissions behave.
/// </summary>
internal static class DemoDataSeeder
{
    private const string DemoPassword = "Demo!Pass2026";

    // Fixed seed so re-running against a fresh database produces the same data, which makes
    // "it looked different yesterday" a real signal rather than noise.
    private static readonly Random Random = new(20260315);

    public static async Task SeedAsync(IServiceProvider provider, ILogger logger)
    {
        var db = provider.GetRequiredService<LtsDbContext>();

        if (await db.Shipments.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding demo data...");

        var countries = await SeedCountriesAsync(db);
        var lookups = await SeedLookupsAsync(db, countries);
        var loadingPoints = await SeedLoadingPointsAsync(db);
        var partners = await SeedPartnersAsync(db);
        var stores = await SeedStoresAsync(db, countries);

        await SeedKpiTargetsAsync(db, countries, lookups);
        await SeedIntegrationAsync(db, countries);
        await SeedShipmentsAsync(db, countries, lookups, loadingPoints, partners, stores);
        await SeedUsersAsync(provider, countries, partners);

        logger.LogInformation("Demo data seeded: {Shipments} shipments, {Transfers} transfers.",
            await db.Shipments.CountAsync(), await db.Transfers.CountAsync());
    }

    private static async Task<List<Country>> SeedCountriesAsync(LtsDbContext db)
    {
        var countries = new List<Country>
        {
            new() { Code = "TR", Name = "Türkiye" },
            new() { Code = "PL", Name = "Poland" }
        };

        db.Countries.AddRange(countries);
        await db.SaveChangesAsync();

        return countries;
    }

    private static async Task<List<LookupValue>> SeedLookupsAsync(LtsDbContext db, List<Country> countries)
    {
        var turkey = countries[0];
        var poland = countries[1];

        var values = new List<LookupValue>
        {
            // Export types are shared across countries.
            new() { Kind = LookupKind.ExportType, Code = "DEF", Name = "Definitive Export", SortOrder = 1 },
            new() { Kind = LookupKind.ExportType, Code = "TRN", Name = "Transit", SortOrder = 2 },
            new() { Kind = LookupKind.ExportType, Code = "TMP", Name = "Temporary Export", SortOrder = 3 },

            new() { Kind = LookupKind.TransportType, Code = "ROAD", Name = "Road", SortOrder = 1 },
            new() { Kind = LookupKind.TransportType, Code = "SEA", Name = "Sea", SortOrder = 2 },
            new() { Kind = LookupKind.TransportType, Code = "AIR", Name = "Air", SortOrder = 3 },
            new() { Kind = LookupKind.TransportType, Code = "RAIL", Name = "Rail", SortOrder = 4 },

            // Customs offices belong to the country that operates them.
            new() { Kind = LookupKind.ArrivalCustoms, CountryId = turkey.Id, Code = "HALKALI", Name = "Halkalı Customs", SortOrder = 1 },
            new() { Kind = LookupKind.ArrivalCustoms, CountryId = turkey.Id, Code = "AMBARLI", Name = "Ambarlı Customs", SortOrder = 2 },
            new() { Kind = LookupKind.ArrivalCustoms, CountryId = turkey.Id, Code = "GEBZE", Name = "Gebze Customs", SortOrder = 3 },
            new() { Kind = LookupKind.ArrivalCustoms, CountryId = poland.Id, Code = "WARSAW", Name = "Warsaw Customs", SortOrder = 1 },
            new() { Kind = LookupKind.ArrivalCustoms, CountryId = poland.Id, Code = "GDANSK", Name = "Gdańsk Customs", SortOrder = 2 }
        };

        db.LookupValues.AddRange(values);
        await db.SaveChangesAsync();

        return values;
    }

    private static async Task<List<LoadingPoint>> SeedLoadingPointsAsync(LtsDbContext db)
    {
        var points = new List<LoadingPoint>
        {
            new() { Code = "DE-HAM", Name = "Hamburg Hub", CountryCode = "DE" },
            new() { Code = "DE-MUC", Name = "Munich Warehouse", CountryCode = "DE" },
            new() { Code = "IT-MIL", Name = "Milan Distribution", CountryCode = "IT" },
            new() { Code = "ES-BCN", Name = "Barcelona Depot", CountryCode = "ES" },
            new() { Code = "CN-SHA", Name = "Shanghai Port", CountryCode = "CN" }
        };

        db.LoadingPoints.AddRange(points);
        await db.SaveChangesAsync();

        return points;
    }

    private static async Task<List<Partner>> SeedPartnersAsync(LtsDbContext db)
    {
        var partners = new List<Partner>
        {
            new() { Type = PartnerType.LogisticsCompany, Code = "TRANSEU", Name = "TransEuro Logistics" },
            new() { Type = PartnerType.LogisticsCompany, Code = "NORDFR", Name = "Nordic Freight" },
            new() { Type = PartnerType.LogisticsCompany, Code = "MEDCAR", Name = "Mediterranean Carriers" },
            new() { Type = PartnerType.Broker, Code = "ATLASB", Name = "Atlas Customs Brokerage" },
            new() { Type = PartnerType.Broker, Code = "MERIDB", Name = "Meridian Broker Services" }
        };

        db.Partners.AddRange(partners);
        await db.SaveChangesAsync();

        return partners;
    }

    private static async Task<List<Store>> SeedStoresAsync(LtsDbContext db, List<Country> countries)
    {
        var names = new[]
        {
            "Central", "Riverside", "Old Town", "Airport", "Northgate",
            "Southpark", "Harbour", "University", "Market Square", "Westfield"
        };

        var stores = new List<Store>();

        foreach (var country in countries)
        {
            for (var i = 0; i < names.Length; i++)
            {
                stores.Add(new Store
                {
                    CountryId = country.Id,
                    Code = $"{country.Code}{100 + i}",
                    Name = $"{names[i]} Store"
                });
            }
        }

        db.Stores.AddRange(stores);
        await db.SaveChangesAsync();

        return stores;
    }

    /// <summary>
    /// A global fallback for every step, plus tighter targets for the busy lanes — the shape a
    /// logistics department's KPI sheet actually has.
    /// </summary>
    private static async Task SeedKpiTargetsAsync(LtsDbContext db, List<Country> countries, List<LookupValue> lookups)
    {
        var definitive = lookups.First(l => l.Kind == LookupKind.ExportType && l.Code == "DEF");
        var turkey = countries[0];
        var effectiveFrom = new DateOnly(2024, 1, 1);

        var defaults = new Dictionary<KpiStep, int>
        {
            [KpiStep.LoadingToExportClearance] = 2,
            [KpiStep.ExportClearanceToDeparture] = 1,
            [KpiStep.DepartureToArrival] = 6,
            [KpiStep.ArrivalToCustomsStart] = 1,
            [KpiStep.CustomsStartToCustomsEnd] = 3,
            [KpiStep.CustomsEndToCrossdockArrival] = 2,
            [KpiStep.CrossdockArrivalToCrossdockDeparture] = 2,
            [KpiStep.CrossdockDepartureToStoreArrival] = 2,
            [KpiStep.StoreArrivalToPreAcceptance] = 1,
            [KpiStep.PreAcceptanceToAcceptance] = 1,
            [KpiStep.TotalLoadingToCrossdockArrival] = 15,
            [KpiStep.TotalLoadingToStoreAcceptance] = 21
        };

        var targets = defaults
            .Select(pair => new KpiTarget
            {
                Step = pair.Key,
                TargetDays = pair.Value,
                EffectiveFrom = effectiveFrom
            })
            .ToList();

        // Germany to Türkiye is a well-run lane, so it is held to tighter numbers.
        targets.AddRange(new[]
        {
            new KpiTarget
            {
                Step = KpiStep.DepartureToArrival,
                ExportTypeId = definitive.Id,
                LoadingCountryCode = "DE",
                ArrivalCountryId = turkey.Id,
                TargetDays = 4,
                EffectiveFrom = effectiveFrom
            },
            new KpiTarget
            {
                Step = KpiStep.CustomsStartToCustomsEnd,
                ExportTypeId = definitive.Id,
                LoadingCountryCode = "DE",
                ArrivalCountryId = turkey.Id,
                TargetDays = 2,
                EffectiveFrom = effectiveFrom
            },
            // Sea freight from China is measured against a realistic transit instead.
            new KpiTarget
            {
                Step = KpiStep.DepartureToArrival,
                LoadingCountryCode = "CN",
                ArrivalCountryId = turkey.Id,
                TargetDays = 30,
                EffectiveFrom = effectiveFrom
            }
        });

        db.KpiTargets.AddRange(targets);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A mock source per country with its status mappings, so the whole integration path —
    /// poll, map, apply, audit — can be exercised before a real endpoint exists.
    /// </summary>
    private static async Task SeedIntegrationAsync(LtsDbContext db, List<Country> countries)
    {
        foreach (var country in countries)
        {
            var source = new IntegrationSource
            {
                CountryId = country.Id,
                Kind = IntegrationSourceKind.Warehouse,
                Name = $"{country.Name} Warehouse (mock)",
                AdapterKey = MockAdapterKey,
                PollIntervalMinutes = 5,
                IsActive = true
            };

            db.IntegrationSources.Add(source);
            await db.SaveChangesAsync();

            // Deliberately unlike the LTS names: this is the point of the mapping table.
            db.StatusMappings.AddRange(
                new StatusMapping
                {
                    IntegrationSourceId = source.Id,
                    RawCode = "WH_IN",
                    RawDescription = "Goods received at crossdock",
                    MilestoneType = MilestoneType.CrossdockArrival
                },
                new StatusMapping
                {
                    IntegrationSourceId = source.Id,
                    RawCode = "WH_OUT",
                    RawDescription = "Dispatched from crossdock",
                    MilestoneType = MilestoneType.CrossdockDeparture
                },
                new StatusMapping
                {
                    IntegrationSourceId = source.Id,
                    RawCode = "ETA_STORE",
                    RawDescription = "Planned store arrival",
                    MilestoneType = MilestoneType.PlannedStoreArrival
                },
                new StatusMapping
                {
                    IntegrationSourceId = source.Id,
                    RawCode = "POD",
                    RawDescription = "Proof of delivery at store",
                    MilestoneType = MilestoneType.StoreArrival
                },
                new StatusMapping
                {
                    IntegrationSourceId = source.Id,
                    RawCode = "WH_SCAN",
                    RawDescription = "Internal handling scan",
                    IsIgnored = true
                });

            await db.SaveChangesAsync();
        }
    }

    /// <summary>Adapter key of the sample-file adapter; matches the registration in the poller.</summary>
    private const string MockAdapterKey = "mock-json";

    private static async Task SeedShipmentsAsync(
        LtsDbContext db,
        List<Country> countries,
        List<LookupValue> lookups,
        List<LoadingPoint> loadingPoints,
        List<Partner> partners,
        List<Store> stores)
    {
        var exportTypes = lookups.Where(l => l.Kind == LookupKind.ExportType).ToList();
        var transportTypes = lookups.Where(l => l.Kind == LookupKind.TransportType).ToList();
        var logisticsCompanies = partners.Where(p => p.Type == PartnerType.LogisticsCompany).ToList();
        var brokers = partners.Where(p => p.Type == PartnerType.Broker).ToList();

        var resolver = new KpiTargetResolver(await db.KpiTargets.AsNoTracking().ToListAsync());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var shipments = new List<Shipment>();
        var sequence = 1;

        foreach (var country in countries)
        {
            var customsOffices = lookups
                .Where(l => l.Kind == LookupKind.ArrivalCustoms && l.CountryId == country.Id)
                .ToList();

            var countryStores = stores.Where(s => s.CountryId == country.Id).ToList();

            for (var i = 0; i < 120; i++)
            {
                var loadingPoint = Pick(loadingPoints);

                // Spread the fleet across the last three months so every lifecycle stage,
                // aging bucket and performance outcome is represented.
                var loadingDate = today.AddDays(-Random.Next(0, 90));
                var stage = Random.Next(0, 12);

                var shipment = new Shipment
                {
                    ReferenceNo = $"REF-2026-{sequence:D5}",
                    InvoiceNo = $"INV-2026-{sequence:D5}",
                    InvoiceDate = loadingDate.AddDays(-1),
                    ArrivalCountryId = country.Id,
                    ArrivalCustomsId = Pick(customsOffices).Id,
                    ExportTypeId = Pick(exportTypes).Id,
                    TransportTypeId = Pick(transportTypes).Id,
                    LoadingPointId = loadingPoint.Id,
                    LoadingPoint = loadingPoint,
                    LogisticsCompanyId = Pick(logisticsCompanies).Id,
                    BrokerId = Pick(brokers).Id,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "seed"
                };

                ApplyLifecycle(shipment, loadingDate, stage);
                AddTransfers(shipment, countryStores, stage);

                ShipmentRecalculator.Recalculate(shipment, resolver, today);
                shipments.Add(shipment);
                sequence++;
            }
        }

        db.Shipments.AddRange(shipments);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fills in dates up to the stage the shipment has reached. Every few shipments a leg is
    /// stretched well past its target so the grids show Late and Overdue rows, not just green.
    /// </summary>
    private static void ApplyLifecycle(Shipment shipment, DateOnly loadingDate, int stage)
    {
        if (stage < 1)
        {
            return;
        }

        var slow = Random.Next(0, 4) == 0;
        var cursor = loadingDate;

        shipment.LoadingDate = cursor;
        if (stage < 2) return;

        cursor = cursor.AddDays(Random.Next(1, slow ? 6 : 3));
        shipment.DepartureCustomsClearanceDate = cursor;
        if (stage < 3) return;

        cursor = cursor.AddDays(Random.Next(0, 2));
        shipment.DepartureDate = cursor;
        if (stage < 4) return;

        cursor = cursor.AddDays(Random.Next(2, slow ? 12 : 6));
        shipment.ArrivalToTargetCountryDate = cursor;
        if (stage < 5) return;

        cursor = cursor.AddDays(Random.Next(0, 3));
        shipment.CustomsStartDate = cursor;
        if (stage < 6) return;

        cursor = cursor.AddDays(Random.Next(1, slow ? 8 : 3));
        shipment.CustomsEndDate = cursor;
        if (stage < 7) return;

        cursor = cursor.AddDays(Random.Next(1, 3));
        shipment.CrossdockArrivalDate = cursor;
    }

    private static void AddTransfers(Shipment shipment, List<Store> stores, int stage)
    {
        // The split is known from the transfer list before the goods physically arrive, so
        // transfers exist even for shipments still in customs.
        var transferCount = Random.Next(2, 6);
        var chosen = stores.OrderBy(_ => Random.Next()).Take(transferCount).ToList();
        var crossdockArrival = shipment.CrossdockArrivalDate;

        foreach (var store in chosen)
        {
            var transfer = new Transfer
            {
                ShipmentId = 0,
                StoreId = store.Id,
                TransferNo = Transfer.BuildTransferNo(shipment.ReferenceNo, store.Code),
                TotalBoxes = Random.Next(5, 120),
                TotalItems = Random.Next(50, 2500),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed"
            };

            if (crossdockArrival is { } arrival && stage >= 8)
            {
                var departure = arrival.AddDays(Random.Next(1, 4));
                transfer.CrossdockDepartureDate = departure;
                transfer.PlannedStoreArrivalDate = departure.AddDays(1);

                if (stage >= 9)
                {
                    transfer.StoreArrivalDate = departure.AddDays(Random.Next(1, 4));

                    if (stage >= 10)
                    {
                        transfer.StorePreAcceptanceDate = transfer.StoreArrivalDate.Value.AddDays(Random.Next(0, 2));

                        if (stage >= 11)
                        {
                            transfer.StoreAcceptanceDate =
                                transfer.StorePreAcceptanceDate.Value.AddDays(Random.Next(0, 3));
                        }
                    }
                }
            }

            shipment.Transfers.Add(transfer);
        }
    }

    /// <summary>
    /// One account per user type, each with the permissions its type implies, so the row and
    /// field restrictions can be checked by simply logging in as each of them.
    /// </summary>
    private static async Task SeedUsersAsync(IServiceProvider provider, List<Country> countries, List<Partner> partners)
    {
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var db = provider.GetRequiredService<LtsDbContext>();

        var logisticsCompany = partners.First(p => p.Type == PartnerType.LogisticsCompany);
        var broker = partners.First(p => p.Type == PartnerType.Broker);

        var accounts = new[]
        {
            (Email: "logistics@lts.local", Name: "Logistics Department User", Type: UserType.LogisticsDepartment, Partner: (int?)null),
            (Email: "carrier@lts.local", Name: $"{logisticsCompany.Name} User", Type: UserType.LogisticsCompany, Partner: (int?)logisticsCompany.Id),
            (Email: "broker@lts.local", Name: $"{broker.Name} User", Type: UserType.Broker, Partner: (int?)broker.Id)
        };

        foreach (var account in accounts)
        {
            if (await users.FindByEmailAsync(account.Email) is not null)
            {
                continue;
            }

            var user = new AppUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                FullName = account.Name,
                UserType = account.Type,
                PartnerId = account.Partner,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed"
            };

            var result = await users.CreateAsync(user, DemoPassword);
            if (!result.Succeeded)
            {
                continue;
            }

            // Cross-country admin pages are granted once with a null country, so the same grant
            // must not be added again for the second country.
            var granted = new HashSet<string>();

            foreach (var country in countries)
            {
                db.UserCountryAccess.Add(new UserCountryAccess { UserId = user.Id, CountryId = country.Id });

                foreach (var grant in PermissionTemplates.For(account.Type))
                {
                    var countryId = PageCatalog.IsCountryScoped(grant.PageKey) ? country.Id : (int?)null;

                    if (!granted.Add(UserPermissions.Key(grant.PageKey, countryId)))
                    {
                        continue;
                    }

                    db.UserPagePermissions.Add(new UserPagePermission
                    {
                        UserId = user.Id,
                        CountryId = countryId,
                        PageKey = grant.PageKey,
                        CanView = grant.CanView,
                        CanEdit = grant.CanEdit
                    });
                }
            }

            await db.SaveChangesAsync();
        }
    }

    private static T Pick<T>(IReadOnlyList<T> items) => items[Random.Next(items.Count)];
}
