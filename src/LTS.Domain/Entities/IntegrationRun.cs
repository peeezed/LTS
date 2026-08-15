using LTS.Domain.Common;
using LTS.Domain.Enums;

namespace LTS.Domain.Entities;

/// <summary>One poll of one integration source, as shown on the integration monitor page.</summary>
public class IntegrationRun : Entity
{
    public required int IntegrationSourceId { get; set; }
    public IntegrationSource? IntegrationSource { get; set; }

    public IntegrationRunStatus Status { get; set; } = IntegrationRunStatus.Running;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public int MessagesReceived { get; set; }
    public int MessagesProcessed { get; set; }
    public int MessagesFailed { get; set; }

    public int ShipmentsCreated { get; set; }
    public int ShipmentsUpdated { get; set; }
    public int TransfersCreated { get; set; }
    public int TransfersUpdated { get; set; }
    public int MilestonesApplied { get; set; }

    /// <summary>
    /// Codes the source sent that have no mapping. Surfaced on the monitor so an admin knows
    /// exactly what to add on the status-mapping page.
    /// </summary>
    public int UnmappedCodeCount { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<IntegrationMessage> Messages { get; set; } = [];

    public TimeSpan? Duration => FinishedAt - StartedAt;
}

/// <summary>
/// One raw payload received from a source, kept verbatim so a failed run can be diagnosed and
/// replayed without asking the source system to resend.
/// </summary>
public class IntegrationMessage : Entity
{
    public required int IntegrationRunId { get; set; }
    public IntegrationRun? IntegrationRun { get; set; }

    /// <summary>Source system's own id for the message, used to avoid reprocessing.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Reference or transfer number the payload is about, when known.</summary>
    public string? EntityReference { get; set; }

    public string? RawStatusCode { get; set; }

    public required string Payload { get; set; }

    public IntegrationMessageStatus Status { get; set; } = IntegrationMessageStatus.Pending;

    public string? ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
