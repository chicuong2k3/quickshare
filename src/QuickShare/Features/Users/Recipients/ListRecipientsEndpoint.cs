namespace QuickShare.Features.Users.Recipients;

using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

public sealed class ListRecipientsEndpoint : EndpointWithoutRequest
{
    private const string RecipientCacheKeyPrefix = "user:recipients:";
    private const int RecentLimit = 20;
    private static readonly TimeSpan RecentTtl = TimeSpan.FromSeconds(30);

    public override void Configure()
    {
        Get("/api/me/recipients");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "List recent recipients + address book contacts";
            s.Description = "Issue #316: recent-recipients quick-pick for the upload page. Only the authenticated user's own contacts.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var cache = Resolve<CacheService>();
        var user = await User.GetUserAsync(db, ct);

        var contacts = await cache.GetOrSetAsync(
            $"{RecipientCacheKeyPrefix}{user.Id}",
            async token => await db.RecipientContacts
                .AsNoTracking()
                .Where(c => c.UserId == user.Id)
                .OrderByDescending(c => c.LastUsedAt)
                .Take(RecentLimit)
                .ToListAsync(token),
            RecentTtl,
            ct);

        var response = contacts.Select(c => c.ToResponse()).ToList();
        await Send.OkAsync(response, ct);
    }
}