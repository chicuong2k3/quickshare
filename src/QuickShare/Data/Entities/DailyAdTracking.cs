namespace QuickShare.Data.Entities;

[DynamicSort]
public record DailyAdTracking
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateOnly Date { get; init; }
    public int AdCount { get; set; }
    public string[] TokenHashes { get; set; } = [];

    public User User { get; init; } = null!;
}
