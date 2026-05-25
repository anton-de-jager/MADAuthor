using MadAuthor.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace MadAuthor.Infrastructure.Email;

public class SmtpEmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 465;
    public bool Secure { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
}

/// <summary>
/// MailKit-backed SMTP sender. If host/user/password are not configured the sender becomes a
/// logged no-op so the rest of the pipeline keeps working in dev without SMTP creds.
/// </summary>
public class SmtpEmailSender(SmtpEmailOptions options, ILogger<SmtpEmailSender> log) : IEmailSender
{
    public async Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Host)
            || string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            log.LogInformation("SMTP not configured - skipping email to {To} subject \"{Subject}\".", toAddress, subject);
            return false;
        }

        var from = string.IsNullOrWhiteSpace(options.FromAddress) ? options.Username : options.FromAddress;
        var fromName = string.IsNullOrWhiteSpace(options.FromName) ? "MADAuthor" : options.FromName;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(fromName, from));
        msg.To.Add(new MailboxAddress(toName, toAddress));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var secure = options.Secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(options.Host, options.Port, secure, ct);
            await client.AuthenticateAsync(options.Username, options.Password, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
            log.LogInformation("Sent email to {To}: \"{Subject}\".", toAddress, subject);
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send email to {To}: \"{Subject}\".", toAddress, subject);
            return false;
        }
    }
}
