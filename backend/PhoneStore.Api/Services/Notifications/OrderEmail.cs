using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhoneStore.Api.Services.Notifications;

// Sensitive envelope for adapters only. Future UI consumes the fragment immediately and clears it.
public sealed record OrderEmail(string MessageKey, string Email, string OrderNumber, string Purpose,
    string? ActionPath, DateTime? ExpiresAt, bool AccountSetup);
public interface IOrderEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(OrderEmail message, CancellationToken ct);
}
public sealed class LocalOrderEmailSender(IHostEnvironment environment, IConfiguration configuration) : IOrderEmailSender
{
    public bool IsConfigured => environment.IsDevelopment();
    public async Task SendAsync(OrderEmail message, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Order email adapter unavailable.");
        var directory = Path.GetFullPath(configuration["OrderEmail:MailboxPath"] ?? Path.Combine(configuration["Auth:MailboxPath"] ?? Path.Combine(environment.ContentRootPath, ".local-mailbox"), "orders"));
        Directory.CreateDirectory(directory);
        var final = Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message.MessageKey))) + ".json");
        if (File.Exists(final)) return;
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(message), ct);
            try { File.Move(temporary, final); }
            catch (IOException) when (File.Exists(final)) { /* Stable event key deduplicates local delivery. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
