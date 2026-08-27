using LTS.Application.Abstractions;
using LTS.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LTS.Infrastructure.Email;

/// <summary>
/// Sends an EmailMessage over SMTP via MailKit, reading Lts:Mail settings. The password is never
/// stored in configuration - it's read via Integration:Secrets:{SecretName}, the same convention
/// ShipmentFeedClient/ExportAttributeFeedClient already use for their bearer tokens.
/// </summary>
public sealed class MailKitEmailSender(IConfiguration configuration, IOptions<LtsOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value.Mail;

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidOperationException("Lts:Mail:Host is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("Lts:Mail:FromAddress is not configured.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName ?? settings.FromAddress, settings.FromAddress));

        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        mime.Subject = message.Subject;

        var builder = new BodyBuilder { TextBody = message.Body };
        foreach (var attachment in message.Attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            settings.Host, settings.Port,
            settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        if (settings.SecretName is { Length: > 0 } secretName)
        {
            var password = configuration[$"Integration:Secrets:{secretName}"];

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Secret '{secretName}' is not configured under Integration:Secrets.");
            }

            await client.AuthenticateAsync(settings.Username ?? settings.FromAddress, password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
