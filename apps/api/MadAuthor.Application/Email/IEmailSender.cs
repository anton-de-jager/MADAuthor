namespace MadAuthor.Application.Email;

public interface IEmailSender
{
    /// <summary>
    /// Send an email. Returns true on successful send, false on a configured no-op (when SMTP
    /// isn't configured locally) or transient failure. Implementations log details.
    /// </summary>
    Task<bool> SendAsync(string toAddress, string toName, string subject, string htmlBody, CancellationToken ct = default);
}
