namespace PhoneStore.Api.Entities;

public class OutboxMessage
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockOwner { get; set; }
    public string? LastError { get; set; }
    public byte[] Version { get; set; } = [];

}

