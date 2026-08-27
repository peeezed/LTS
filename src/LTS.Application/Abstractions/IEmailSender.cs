namespace LTS.Application.Abstractions;

/// <summary>One file attached to an outgoing email - e.g. a delay alert's generated Excel report.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>One outgoing email, transport-agnostic.</summary>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string Body,
    IReadOnlyList<EmailAttachment> Attachments);

/// <summary>
/// Sends an email. A general capability, not delay-alert-specific, injected so callers stay
/// testable without a real SMTP server.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
