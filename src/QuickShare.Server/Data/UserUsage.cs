namespace QuickShare.Server.Data;

[DynamicSort]
public record UserUsage
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime PeriodStart { get; init; }
    public long TotalBytesUploaded { get; set; }
    public int TransferCount { get; set; }

    public User User { get; init; } = null!;
}
