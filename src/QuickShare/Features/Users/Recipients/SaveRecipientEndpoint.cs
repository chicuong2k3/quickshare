namespace QuickShare.Features.Users.Recipients;

using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

public sealed class SaveRecipientEndpoint : Endpoint<SaveRecipientRequest>
{
    private const string RecipientCacheKeyPrefix = "user:recipients:";
    private const int NameMaxLength = 100;

    public override void Configure()
    {
        Post("/api/me/recipients");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Save or update an address-book contact";
            s.Description = "Issue #316: name an existing recent recipient or add a new address-book entry. Upsert by (user, email).";
        });
    }

    public override async Task HandleAsync(SaveRecipientRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var cache = Resolve<CacheService>();
        var user = await User.GetUserAsync(db, ct);

        var email = req.Email.Trim().ToLowerInvariant();
        if (!email.Contains('@') || email.Length > 254)
        {
            ThrowError("Invalid email", 400);
            return;
        }

        var name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
        if (name is { Length: > NameMaxLength })
        {
            ThrowError($"Name exceeds {NameMaxLength} characters", 400);
            return;
        }

        var contact = await db.RecipientContacts
            .FirstOrDefaultAsync(c => c.UserId == user.Id && c.Email == email, ct);

        if (contact is null)
        {
            contact = new RecipientContact
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Email = email,
                Name = name,
                LastUsedAt = DateTime.UtcNow,
            };
            db.RecipientContacts.Add(contact);
        }
        else
        {
            contact.Name = name;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"{RecipientCacheKeyPrefix}{user.Id}", ct);

        await Send.OkAsync(contact.ToResponse(), ct);
    }
}

public class SaveRecipientRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}