namespace LTS.Application.ExportAttributeFeed;

/// <summary>
/// One response from GetLTSExportFileDetail - the four KPI-scoping attributes plus carrier/broker
/// for one shipment, keyed by ExportFileNumber (the same value LTS_Shipments.ReferenceNo already
/// holds - see ShipmentFeed's InvoiceListEntryDto.ExportNumber, a different upstream name for the
/// same thing). A different upstream naming convention (flat PascalCase, no underscores) from
/// InvoiceListEntryDto's Arrival_Customs-style fields, so it needs its own DTO rather than reusing
/// that one; relies on the same PropertyNameCaseInsensitive convention used everywhere else in
/// this codebase.
/// </summary>
public sealed record ExportFileDetailDto(
    string ExportFileNumber,
    string? ArrivalCustoms,
    string? ArrivalCustomsDesc,
    string? ExportType,
    string? ExportTypeDesc,
    string? Transport,
    string? TransportDesc,
    string? LoadingPoint,
    string? LoadingPointDesc,
    string? Carier,
    string? CarierDesc,
    string? BrokerCompany,
    string? BrokerCompanyDesc);

/// <summary>The resolved (Description-form) attributes this module writes onto LtsIntegrationShipment.</summary>
public sealed record StandardizedExportAttributes(
    string ReferenceNo,
    string? ArrivalCustoms,
    string? ExportType,
    string? TransportType,
    string? LoadingPoint,
    string? LogisticsCompany,
    string? BrokerCompany,
    IReadOnlyList<string> Warnings);
