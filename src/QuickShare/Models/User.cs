namespace QuickShare.Models;

[DynamicSort]
public record User : BaseAuditEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Plan { get; set; } = Constants.PlanTierLimits.Free;
}
