namespace QuickShare.Services;

public sealed class TrialService
{
    private const int DefaultTrialLimit = 5;

    public async Task<int> GetRemainingTrialsAsync(User user, UserUsage usage, CancellationToken ct = default)
    {
        var trialAllowance = user.Plan == PlanTierLimits.Free ? DefaultTrialLimit : 0;
        if (trialAllowance <= 0) return 0;
        var used = await Task.FromResult(usage.TransferCount);
        return Math.Max(0, trialAllowance - used);
    }

    public async Task<bool> IsInTrialAsync(User user, UserUsage usage, CancellationToken ct = default)
    {
        if (user.Plan != PlanTierLimits.Free) return false;
        var remaining = await GetRemainingTrialsAsync(user, usage, ct);
        return remaining > 0;
    }
}
