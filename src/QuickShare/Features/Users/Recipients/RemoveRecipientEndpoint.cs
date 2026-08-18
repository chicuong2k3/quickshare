namespace QuickShare.Features.Users.Recipients;

using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

public sealed class RemoveRecipientEndpoint : EndpointWithoutRequest
{
    private const string RecipientIdRouteKey = "id";
    private const string RecipientCacheKeyPrefix = "user:recipients:";

    public override void Configure()
    {
        Delete("/api/me/recipients/{id}");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Remove a recipient contact";
            s.Description = "Issue #316: removes an address-book entry or a captured recent recipient. Only the authenticated user's own contacts.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var cache = Resolve<CacheService>();
        var user = await User.GetUserAsync(db, ct);

        var contactId = Route<Guid>(RecipientIdRouteKey);

        var contact = await db.RecipientContacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.UserId == user.Id, ct);

        if (contact is null)
        {
            ThrowError("Recipient not found", 404);
            return;
        }

        db.RecipientContacts.Remove(contact);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"{RecipientCacheKeyPrefix}{user.Id}", ct);

        await Send.OkAsync(new { removed = true }, ct);
    }
}