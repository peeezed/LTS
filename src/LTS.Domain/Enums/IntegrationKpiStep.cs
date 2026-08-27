namespace LTS.Domain.Enums;

/// <summary>
/// The seven KPI legs LTS_Integration scores a shipment/transfer against - a simplified model
/// matching the KPI*Date deadline columns already present in
/// LTS_ShipmentDates/LTS_ShipmentTransferDates, scoped by Country + Export Type + Loading Point +
/// Arrival Customs + Transport Type. Kept separate from the old <see cref="KpiStep"/>, which
/// belongs to the dead LtsDbContext system and scores a different, 10-leg model.
/// </summary>
public enum IntegrationKpiStep
{
    LoadingToCustomsClearance,
    CustomsToDeparture,
    InternationalTransportation,
    CountryCustomsClearance,
    LeadTimeToXdock,
    Xdock,

    /// <summary>Crossdock Departure to Store Arrival - fully transfer-scope, unlike Xdock's shipment/transfer split.</summary>
    LocalTransportation
}
