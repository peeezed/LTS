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

/// <summary>Which external system an integration source represents.</summary>
public enum IntegrationSourceKind
{
    /// <summary>In-house company service: shipment master data, transfer split, box/item counts, store acceptance.</summary>
    InHouseShipmentService = 1,

    /// <summary>Country warehouse system: crossdock and store movement events.</summary>
    Warehouse = 2
}

/// <summary>Outcome of a single integration poll.</summary>
public enum IntegrationRunStatus
{
    Running = 1,
    Succeeded = 2,
    PartiallySucceeded = 3,
    Failed = 4
}

/// <summary>Processing state of one raw payload received from an integration source.</summary>
public enum IntegrationMessageStatus
{
    Pending = 1,
    Processed = 2,
    Skipped = 3,
    Failed = 4
}
