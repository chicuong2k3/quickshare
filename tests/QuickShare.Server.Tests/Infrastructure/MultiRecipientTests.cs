using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class MultiRecipientTests
{
    [Fact]
    public void RecipientLimit_FreeThree_PaidTwenty()
    {
        Assert.Equal(3, CommitR2Handler.RecipientLimit(PlanTierLimits.Free));
        Assert.Equal(3, CommitR2Handler.RecipientLimit(PlanTierLimits.Anonymous));
        Assert.Equal(20, CommitR2Handler.RecipientLimit(PlanTierLimits.Pro));
        Assert.Equal(20, CommitR2Handler.RecipientLimit(PlanTierLimits.Studio));
    }

    [Fact]
    public void NormalizeRecipients_TrimsDedupesFiltersAndLowercases()
    {
        var result = CommitR2Handler.NormalizeRecipients(
            ["  A@Example.COM ", "a@example.com", "not-an-email", "b@x.io"]);

        Assert.Equal(["a@example.com", "b@x.io"], result);
    }

    [Fact]
    public void NormalizeRecipients_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(CommitR2Handler.NormalizeRecipients(null));
        Assert.Empty(CommitR2Handler.NormalizeRecipients([]));
    }
}
