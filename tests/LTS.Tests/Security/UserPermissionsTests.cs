using FluentAssertions;
using LTS.Application.Security;
using LTS.Domain.Enums;
using LTS.Domain.Security;

namespace LTS.Tests.Security;

public class UserPermissionsTests
{
    private const int Turkey = 1;
    private const int Poland = 2;
    private const int AtlasBroker = 10;

    private static UserPermissions Build(
        UserType userType,
        int? partnerId = null,
        int[]? countries = null,
        params (string PageKey, int? CountryId, bool CanView, bool CanEdit)[] grants)
    {
        var pages = grants.ToDictionary(
            g => UserPermissions.Key(g.PageKey, g.CountryId),
            g => new PagePermission(g.CanView, g.CanEdit));

        return new UserPermissions(Guid.NewGuid(), userType, partnerId, null, countries ?? [Turkey], pages);
    }

    [Fact]
    public void An_admin_bypasses_the_grant_tables_entirely()
    {
        var permissions = Build(UserType.Admin, countries: []);

        permissions.HasCountry(Poland).Should().BeTrue();
        permissions.CanView(PageKeys.AdminUsers, null).Should().BeTrue();
        permissions.CanEdit(PageKeys.ShipmentDetails, Poland).Should().BeTrue();
    }

    [Fact]
    public void A_grant_in_one_country_does_not_leak_into_another()
    {
        var permissions = Build(UserType.LogisticsDepartment, countries: [Turkey, Poland],
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        permissions.CanEdit(PageKeys.ShipmentDetails, Turkey).Should().BeTrue();
        permissions.CanEdit(PageKeys.ShipmentDetails, Poland).Should().BeFalse();
        permissions.CanView(PageKeys.ShipmentDetails, Poland).Should().BeFalse();
    }

    [Fact]
    public void A_grant_for_a_country_the_user_cannot_enter_is_worthless()
    {
        // The country grant is the outer gate: a page grant behind it must not open on its own.
        var permissions = Build(UserType.LogisticsDepartment, countries: [Turkey],
            grants: (PageKeys.Shipments, Poland, true, true));

        permissions.CanView(PageKeys.Shipments, Poland).Should().BeFalse();
    }

    [Fact]
    public void Brokers_and_logistics_companies_are_partner_scoped_but_internal_staff_are_not()
    {
        Build(UserType.Broker, AtlasBroker).IsPartnerScoped.Should().BeTrue();
        Build(UserType.LogisticsCompany, 20).IsPartnerScoped.Should().BeTrue();
        Build(UserType.LogisticsDepartment).IsPartnerScoped.Should().BeFalse();
        Build(UserType.Admin).IsPartnerScoped.Should().BeFalse();
    }

    [Fact]
    public void A_broker_sees_only_its_own_date_fields_on_the_details_page()
    {
        var broker = Build(UserType.Broker, AtlasBroker);

        broker.CanViewMilestone(MilestoneType.CustomsStart).Should().BeTrue();
        broker.CanViewMilestone(MilestoneType.CustomsEnd).Should().BeTrue();
        broker.CanViewMilestone(MilestoneType.Loading).Should().BeFalse();
        broker.CanViewMilestone(MilestoneType.Departure).Should().BeFalse();
    }

    [Fact]
    public void A_logistics_company_sees_only_its_own_date_fields()
    {
        var carrier = Build(UserType.LogisticsCompany, 20);

        carrier.CanViewMilestone(MilestoneType.Loading).Should().BeTrue();
        carrier.CanViewMilestone(MilestoneType.ArrivalToTargetCountry).Should().BeTrue();
        carrier.CanViewMilestone(MilestoneType.CustomsStart).Should().BeFalse();
    }

    [Fact]
    public void Internal_staff_see_every_date_field()
    {
        var staff = Build(UserType.LogisticsDepartment);

        foreach (var milestone in LTS.Domain.Milestones.MilestoneCatalog.All)
        {
            staff.CanViewMilestone(milestone.Type).Should().BeTrue();
        }
    }

    [Fact]
    public void Editing_a_milestone_needs_edit_rights_on_the_details_page()
    {
        var withoutEdit = Build(UserType.Broker, AtlasBroker,
            grants: (PageKeys.ShipmentDetails, Turkey, true, false));

        withoutEdit.CanEditMilestone(MilestoneType.CustomsStart, Turkey).Should().BeFalse();

        var withEdit = Build(UserType.Broker, AtlasBroker,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        withEdit.CanEditMilestone(MilestoneType.CustomsStart, Turkey).Should().BeTrue();
    }

    [Fact]
    public void A_broker_cannot_write_a_logistics_companys_dates_even_with_edit_rights()
    {
        var broker = Build(UserType.Broker, AtlasBroker,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        broker.CanEditMilestone(MilestoneType.Loading, Turkey).Should().BeFalse();
        broker.CanEditMilestone(MilestoneType.Departure, Turkey).Should().BeFalse();
    }

    [Fact]
    public void Warehouse_dates_are_for_internal_staff_not_for_partners()
    {
        var broker = Build(UserType.Broker, AtlasBroker,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));
        var staff = Build(UserType.LogisticsDepartment,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        broker.CanEditMilestone(MilestoneType.CrossdockArrival, Turkey).Should().BeFalse();
        staff.CanEditMilestone(MilestoneType.CrossdockArrival, Turkey).Should().BeTrue();
    }

    [Fact]
    public void In_house_service_dates_can_never_be_typed_in_by_anyone()
    {
        var admin = Build(UserType.Admin);
        var staff = Build(UserType.LogisticsDepartment,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        admin.CanEditMilestone(MilestoneType.StorePreAcceptance, Turkey).Should().BeFalse();
        admin.CanEditMilestone(MilestoneType.StoreAcceptance, Turkey).Should().BeFalse();
        staff.CanEditMilestone(MilestoneType.StoreAcceptance, Turkey).Should().BeFalse();
    }

    [Fact]
    public void Editable_milestones_lists_exactly_what_a_partner_may_enter()
    {
        var carrier = Build(UserType.LogisticsCompany, 20,
            grants: (PageKeys.ShipmentDetails, Turkey, true, true));

        carrier.EditableMilestones(Turkey).Select(m => m.Type).Should().BeEquivalentTo(
        [
            MilestoneType.Loading,
            MilestoneType.DepartureCustomsClearance,
            MilestoneType.Departure,
            MilestoneType.ArrivalToTargetCountry
        ]);
    }

    [Fact]
    public void An_unauthenticated_visitor_has_nothing()
    {
        UserPermissions.None.HasCountry(Turkey).Should().BeFalse();
        UserPermissions.None.CanView(PageKeys.Shipments, Turkey).Should().BeFalse();
        UserPermissions.None.CanEditMilestone(MilestoneType.Loading, Turkey).Should().BeFalse();
    }

    [Fact]
    public void Cross_country_admin_grants_do_not_collide_with_country_scoped_ones()
    {
        UserPermissions.Key(PageKeys.AdminUsers, null)
            .Should().NotBe(UserPermissions.Key(PageKeys.AdminUsers, Turkey));
    }
}
