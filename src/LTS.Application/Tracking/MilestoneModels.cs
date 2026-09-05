using LTS.Domain.Enums;

namespace LTS.Application.Tracking;

/// <summary>One date to write against one shipment or transfer.</summary>
/// <param name="Reference">
/// Reference number or invoice number for shipment milestones; transfer number
/// ("{ReferenceNo}_{StoreCode}") for transfer milestones.
/// </param>
/// <param name="Type">Which milestone the date belongs to.</param>
/// <param name="Date">The date, or null to clear the milestone.</param>
public sealed record MilestoneChange(string Reference, MilestoneType Type, DateOnly? Date);

/// <summary>How a batch of changes should be applied.</summary>
/// <param name="Source">Where the values came from; recorded on every audit row.</param>
/// <param name="EnforcePermissions">
/// False only for integration and seeding, which act as the system rather than as a person.
/// </param>
/// <param name="ManualOverrideWins">
/// When true, a value that was entered by hand is left alone and the incoming value is only
/// audited. Used by integration sources configured to defer to manual entry.
/// </param>
/// <param name="IntegrationRunId">The poll that produced these values, for traceability.</param>
/// <param name="Note">Free text stored on the audit row, e.g. the uploaded file name.</param>
/// <param name="SkipChronologyValidation">
/// When true, skips the same-owner "the previous milestone in this chain must already have a
/// date" check (still enforces the future-date sanity check). For a verified automated source
/// whose dates must apply independently of whether a human has entered an earlier milestone in
/// the same owner chain yet - e.g. the Romania KLG feed writing Crossdock Departure/Store Arrival
/// dates regardless of whether Crossdock Arrival has been typed in. False for every other caller.
/// </param>
public sealed record MilestoneApplyOptions(
    MilestoneSource Source,
    bool EnforcePermissions = true,
    bool ManualOverrideWins = false,
    int? IntegrationRunId = null,
    string? Note = null,
    bool SkipChronologyValidation = false)
{
    public static MilestoneApplyOptions Manual => new(MilestoneSource.Manual);
}

/// <summary>Why a single change could not be applied.</summary>
/// <param name="Reference">The reference the change was for.</param>
/// <param name="Type">The milestone it targeted.</param>
/// <param name="Message">Message shown to the user, e.g. in the upload preview.</param>
public sealed record MilestoneError(string Reference, MilestoneType? Type, string Message);

/// <summary>Outcome of applying a batch of changes.</summary>
/// <param name="Applied">Values actually written.</param>
/// <param name="Unchanged">Values that already matched, so nothing was written.</param>
/// <param name="Errors">Changes that were rejected, with the reason.</param>
public sealed record MilestoneApplyResult(
    int Applied,
    int Unchanged,
    IReadOnlyList<MilestoneError> Errors)
{
    public static readonly MilestoneApplyResult Empty = new(0, 0, []);

    public bool HasErrors => Errors.Count > 0;

    public MilestoneApplyResult Combine(MilestoneApplyResult other) =>
        new(Applied + other.Applied, Unchanged + other.Unchanged, [.. Errors, .. other.Errors]);
}
