using LTS.Application.Abstractions;
using LTS.Application.Security;
using LTS.Application.Tracking;
using LTS.Domain.Entities;
using LTS.Domain.Enums;
using LTS.Domain.Kpi;
using LTS.Domain.Security;
using LTS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LTS.Tests;

/// <summary>
/// Builds a small in-memory database with the shape the scoping and milestone rules care about:
/// two countries, two brokers, two carriers, and shipments spread across them.
/// </summary>
internal static class TestDb
{
    public const int Turkey = 1;
    public const int Poland = 2;
    public const int AtlasBroker = 10;
    public const int MeridianBroker = 11;
    public const int TransEuro = 20;
    public const int NordicFreight = 21;
    public const int StoreTr100 = 30;

    public static LtsDbContext Create(ICurrentUser? currentUser = null, IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<LtsDbContext>()
            .UseInMemoryDatabase($"lts-tests-{Guid.NewGuid()}")
            .Options;

        var db = new LtsDbContext(options, currentUser, clock ?? new FixedClock());
        Seed(db);

        return db;
    }

    private static void Seed(LtsDbContext db)
    {
        db.Countries.AddRange(
            new Country { Id = Turkey, Code = "TR", Name = "Türkiye" },
            new Country { Id = Poland, Code = "PL", Name = "Poland" });

        db.Partners.AddRange(
            new Partner { Id = AtlasBroker, Type = PartnerType.Broker, Code = "ATLAS", Name = "Atlas Brokerage" },
            new Partner { Id = MeridianBroker, Type = PartnerType.Broker, Code = "MERID", Name = "Meridian Broker" },
            new Partner { Id = TransEuro, Type = PartnerType.LogisticsCompany, Code = "TRANS", Name = "TransEuro" },
            new Partner { Id = NordicFreight, Type = PartnerType.LogisticsCompany, Code = "NORD", Name = "Nordic Freight" });

        db.LoadingPoints.Add(new LoadingPoint { Id = 40, Code = "DE-HAM", Name = "Hamburg", CountryCode = "DE" });
        db.Stores.Add(new Store { Id = StoreTr100, CountryId = Turkey, Code = "TR100", Name = "Central Store" });

        // Atlas + TransEuro in Türkiye, Meridian + Nordic in Türkiye, and one shipment in Poland,
        // so every scoping rule has something it must exclude.
        AddShipment(db, 100, "REF-A", Turkey, AtlasBroker, TransEuro);
        AddShipment(db, 101, "REF-B", Turkey, AtlasBroker, NordicFreight);
        AddShipment(db, 102, "REF-C", Turkey, MeridianBroker, TransEuro);
        AddShipment(db, 103, "REF-D", Poland, AtlasBroker, TransEuro);

        db.SaveChanges();
    }

    private static void AddShipment(LtsDbContext db, int id, string reference, int countryId, int brokerId, int carrierId)
    {
        var shipment = new Shipment
        {
            Id = id,
            ReferenceNo = reference,
            InvoiceNo = $"INV-{reference}",
            InvoiceDate = new DateOnly(2026, 3, 1),
            ArrivalCountryId = countryId,
            BrokerId = brokerId,
            LogisticsCompanyId = carrierId,
            LoadingPointId = 40
        };

        shipment.Transfers.Add(new Transfer
        {
            Id = id * 10,
            ShipmentId = id,
            StoreId = StoreTr100,
            TransferNo = Transfer.BuildTransferNo(reference, "TR100"),
            TotalBoxes = 10,
            TotalItems = 100
        });

        db.Shipments.Add(shipment);
    }

    public static UserPermissions Permissions(
        UserType userType, int? partnerId = null, int[]? countries = null, bool canEdit = true)
    {
        var pages = new Dictionary<string, PagePermission>
        {
            [UserPermissions.Key(PageKeys.ShipmentDetails, Turkey)] = new(true, canEdit),
            [UserPermissions.Key(PageKeys.ShipmentDetails, Poland)] = new(true, canEdit),
            [UserPermissions.Key(PageKeys.Shipments, Turkey)] = new(true, false)
        };

        return new UserPermissions(Guid.NewGuid(), userType, partnerId, countries ?? [Turkey, Poland], pages);
    }
}

/// <summary>A clock frozen at a known date so KPI scoring in tests is repeatable.</summary>
internal sealed class FixedClock(DateOnly? today = null) : IClock
{
    public DateOnly Today { get; } = today ?? new DateOnly(2026, 3, 20);

    public DateTime UtcNow => Today.ToDateTime(new TimeOnly(12, 0));
}

/// <summary>A current user that reports whoever the test says is signed in.</summary>
internal sealed class TestCurrentUser(Guid? userId = null, string? userName = "tester", int? partnerId = null)
    : ICurrentUser
{
    public bool IsAuthenticated => userId is not null;
    public Guid? UserId => userId;
    public string? UserName => userName;
    public string? FullName => userName;
    public UserType? UserType => null;
    public int? PartnerId => partnerId;
    public bool IsAdmin => false;
}

/// <summary>Supplies a fixed set of KPI targets without going near the database.</summary>
internal sealed class StubKpiTargetProvider(params KpiTarget[] targets) : IKpiTargetProvider
{
    private readonly KpiTargetResolver _resolver = new(targets);

    public Task<KpiTargetResolver> GetResolverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_resolver);

    public void Invalidate() { }
}
