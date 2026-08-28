namespace LTS.Domain.Enums;

/// <summary>External company types that LTS tracks and that external users belong to.</summary>
public enum PartnerType
{
    LogisticsCompany = 1,
    Broker = 2
}

/// <summary>
/// The kinds of simple code/name reference lists. Adding a new attribute list for a new
/// country means adding rows, not tables.
/// </summary>
public enum LookupKind
{
    ArrivalCustoms = 1,
    ExportType = 2,
    TransportType = 3
}

