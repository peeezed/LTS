using FluentAssertions;
using LTS.Application.Security;
using LTS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LTS.Tests.Security;

/// <summary>
/// Row-level scoping is the restriction a broker or carrier cannot talk their way around, so it
/// is tested against a real query rather than against the permission object alone.
/// </summary>
public class ShipmentScopeTests
{
    [Fact]
    public async Task Internal_staff_see_every_shipment_in_the_country_they_are_in()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        var references = await db.Shipments.Scoped(TestDb.Turkey, permissions)
            .Select(s => s.ReferenceNo).ToListAsync();

        references.Should().BeEquivalentTo(["REF-A", "REF-B", "REF-C"]);
    }

    [Fact]
    public async Task The_other_countrys_shipments_are_never_included()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.LogisticsDepartment);

        var references = await db.Shipments.Scoped(TestDb.Poland, permissions)
            .Select(s => s.ReferenceNo).ToListAsync();

        references.Should().BeEquivalentTo(["REF-D"]);
    }

    [Fact]
    public async Task A_broker_sees_only_the_shipments_it_is_the_broker_for()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker);

        var references = await db.Shipments.Scoped(TestDb.Turkey, permissions)
            .Select(s => s.ReferenceNo).ToListAsync();

        references.Should().BeEquivalentTo(["REF-A", "REF-B"],
            "REF-C belongs to the other broker");
    }

    [Fact]
    public async Task A_logistics_company_sees_only_the_shipments_it_carries()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.LogisticsCompany, TestDb.TransEuro);

        var references = await db.Shipments.Scoped(TestDb.Turkey, permissions)
            .Select(s => s.ReferenceNo).ToListAsync();

        references.Should().BeEquivalentTo(["REF-A", "REF-C"],
            "REF-B is carried by the other logistics company");
    }

    [Fact]
    public async Task A_broker_and_a_carrier_on_the_same_shipment_each_see_it_from_their_own_side()
    {
        using var db = TestDb.Create();

        var brokerView = await db.Shipments
            .Scoped(TestDb.Turkey, TestDb.Permissions(UserType.Broker, TestDb.MeridianBroker))
            .Select(s => s.ReferenceNo).ToListAsync();

        var carrierView = await db.Shipments
            .Scoped(TestDb.Turkey, TestDb.Permissions(UserType.LogisticsCompany, TestDb.NordicFreight))
            .Select(s => s.ReferenceNo).ToListAsync();

        brokerView.Should().BeEquivalentTo(["REF-C"]);
        carrierView.Should().BeEquivalentTo(["REF-B"]);
    }

    [Fact]
    public async Task An_external_account_with_no_partner_sees_nothing_rather_than_everything()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.Broker, partnerId: null);

        var references = await db.Shipments.Scoped(TestDb.Turkey, permissions)
            .Select(s => s.ReferenceNo).ToListAsync();

        references.Should().BeEmpty();
    }

    [Fact]
    public async Task Transfers_inherit_their_shipments_scope()
    {
        using var db = TestDb.Create();
        var permissions = TestDb.Permissions(UserType.Broker, TestDb.AtlasBroker);

        var transfers = await db.Transfers.Scoped(TestDb.Turkey, permissions)
            .Select(t => t.TransferNo).ToListAsync();

        transfers.Should().BeEquivalentTo(["REF-A_TR100", "REF-B_TR100"]);
    }
}
