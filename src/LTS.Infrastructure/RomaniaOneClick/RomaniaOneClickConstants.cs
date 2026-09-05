namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>
/// Which KLG OneClick call a staged LTS_ShipmentFeedStaging row came from - the same table and
/// Pending/Processed/Failed lifecycle the internal shipment feed already uses (see
/// ShipmentFeedEndpointKinds in ShipmentFeedRunner.cs), so every integration's call log lives in
/// one place.
/// </summary>
internal static class RomaniaOneClickEndpointKinds
{
    public const string DomesticShipmentLookup = "RomaniaDomesticShipmentLookup";
    public const string TokenRefresh = "RomaniaTokenRefresh";
}

/// <summary>
/// LTS_ShipmentFeedStaging.CustomerCode is a required column, but KLG OneClick has no customer-code
/// concept (the API key itself scopes calls to one company) - every Romania staging row uses this
/// fixed sentinel instead, with the real KLG perm_shipment_id in ReferenceNo where applicable.
/// </summary>
internal static class RomaniaConstants
{
    public const string CustomerCodeSentinel = "ROMANIA";
}
