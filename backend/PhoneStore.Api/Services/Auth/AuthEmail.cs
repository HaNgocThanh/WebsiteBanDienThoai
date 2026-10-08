using System.Text.Json;

namespace PhoneStore.Api.Services.Auth;

// Sensitive local/test mailbox envelope; never returned by public API or written to logs.
public sealed record AuthEmail(string Purpose, string Email, Guid UserId, string Token, DateTime ExpiresAt);

public interface IAuthEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(AuthEmail message, CancellationToken cancellationToken);
}

public sealed class LocalAuthEmailSender(IHostEnvironment environment, IConfiguration configuration) : IAuthEmailSender
{
    public bool IsConfigured => environment.IsDevelopment();

    public async Task SendAsync(AuthEmail message, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Configure an email adapter before enabling auth email flows.");
        var directory = Path.GetFullPath(configuration["Auth:MailboxPath"] ?? Path.Combine(environment.ContentRootPath, ".local-mailbox"));
        Directory.CreateDirectory(directory);
        // Unique file and atomic rename keep partially written messages out of the mailbox.
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        var final = Path.ChangeExtension(temporary, ".json");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(message), cancellationToken);
            File.Move(temporary, final);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
