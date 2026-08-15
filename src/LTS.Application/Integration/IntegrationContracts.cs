namespace LTS.Application.Integration;

/// <summary>
/// A shipment as an external system describes it, in codes rather than LTS identifiers. This is
/// the canonical shape every adapter normalises to, which is what keeps the rest of LTS free of
/// any knowledge of the source systems.
/// </summary>
public sealed record ShipmentSnapshotDto
{
    public required string ReferenceNo { get; init; }
    public required string InvoiceNo { get; init; }
    public DateOnly InvoiceDate { get; init; }

    /// <summary>ISO alpha-2 code of the receiving country.</summary>
    public required string ArrivalCountryCode { get; init; }

    public string? ArrivalCustomsCode { get; init; }
    public string? ExportTypeCode { get; init; }
    public string? TransportTypeCode { get; init; }
    public string? LoadingPointCode { get; init; }
    public string? LogisticsCompanyCode { get; init; }
    public string? BrokerCode { get; init; }

    /// <summary>The transfer split. Empty when the source has not published it yet.</summary>
    public IReadOnlyList<TransferSnapshotDto> Transfers { get; init; } = [];
}

/// <summary>One store's share of a shipment, as published by the source system.</summary>
public sealed record TransferSnapshotDto
{
    public required string StoreCode { get; init; }
    public int TotalBoxes { get; init; }
    public int TotalItems { get; init; }
}

/// <summary>
/// A status event from a source system. The raw code is deliberately kept as-is: what it means
/// in LTS is decided by the admin-editable status mapping, not by the adapter.
/// </summary>
public sealed record MilestoneEventDto
{
    /// <summary>Reference number, invoice number, or transfer number for store-level events.</summary>
    public required string Reference { get; init; }

    /// <summary>The source system's own status code, mapped to an LTS milestone on arrival.</summary>
    public required string RawStatusCode { get; init; }

    public required DateOnly EventDate { get; init; }

    /// <summary>Source system's id for the event, used to avoid processing it twice.</summary>
    public string? ExternalId { get; init; }

    /// <summary>The original payload, archived so a failed run can be diagnosed and replayed.</summary>
    public string? RawPayload { get; init; }
}

/// <summary>Everything one poll of a source produced.</summary>
/// <param name="Shipments">Master data and transfer splits to create or update.</param>
/// <param name="Events">Status events to map onto milestones.</param>
/// <param name="Cursor">
/// Opaque marker handed back on the next poll so the source can return only what is new.
/// Null leaves the stored cursor untouched.
/// </param>
public sealed record IntegrationFetchResult(
    IReadOnlyList<ShipmentSnapshotDto> Shipments,
    IReadOnlyList<MilestoneEventDto> Events,
    string? Cursor = null)
{
    public static readonly IntegrationFetchResult Empty = new([], []);

    public int TotalMessages => Shipments.Count + Events.Count;
}

/// <summary>What an adapter is told about the source it is being asked to poll.</summary>
/// <param name="SourceId">The integration source row.</param>
/// <param name="CountryId">The LTS country the source belongs to.</param>
/// <param name="CountryCode">That country's ISO code, for building requests.</param>
/// <param name="BaseUrl">Endpoint from the source configuration.</param>
/// <param name="SecretName">Name of the configuration entry holding the credential.</param>
/// <param name="SettingsJson">Adapter-specific settings.</param>
/// <param name="Cursor">Cursor returned by the previous successful poll.</param>
public sealed record IntegrationContext(
    int SourceId,
    int CountryId,
    string CountryCode,
    string? BaseUrl,
    string? SecretName,
    string? SettingsJson,
    string? Cursor);

/// <summary>
/// A country's connection to one external system. Adding a country means writing one of these
/// and adding its configuration rows — nothing in the domain, the KPI engine or the UI changes.
/// </summary>
public interface ICountryIntegrationAdapter
{
    /// <summary>Matches <c>IntegrationSource.AdapterKey</c>, which is how this adapter is selected.</summary>
    string AdapterKey { get; }

    Task<IntegrationFetchResult> FetchAsync(IntegrationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Finds the adapter configured for a source.</summary>
public interface IIntegrationAdapterRegistry
{
    ICountryIntegrationAdapter? Find(string adapterKey);

    IReadOnlyList<string> RegisteredKeys { get; }
}
