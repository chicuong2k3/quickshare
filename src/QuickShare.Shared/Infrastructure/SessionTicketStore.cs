using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Shared.Models;

namespace QuickShare.Shared.Infrastructure;

public sealed class SessionTicketStore : ITicketStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _expiration;

    public SessionTicketStore(IServiceScopeFactory scopeFactory, TimeSpan expiration)
    {
        _scopeFactory = scopeFactory;
        _expiration = expiration;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISessionTicketDbContext>();

        var key = Guid.NewGuid().ToString("N");
        var serialized = TicketSerializer.Default.Serialize(ticket);

        db.SessionTickets.Add(new SessionTicket
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            Value = serialized,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_expiration),
            SchemeName = ticket.AuthenticationScheme
        });
        await db.SaveChangesAsync();

        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISessionTicketDbContext>();

        var existing = await db.SessionTickets.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is not null)
        {
            db.SessionTickets.Remove(existing);
        }

        var serialized = TicketSerializer.Default.Serialize(ticket);
        db.SessionTickets.Add(new SessionTicket
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            Value = serialized,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_expiration),
            SchemeName = ticket.AuthenticationScheme
        });
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISessionTicketDbContext>();

        var session = await db.SessionTickets
            .FirstOrDefaultAsync(s => s.Key == key);

        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt < DateTimeOffset.UtcNow)
        {
            db.SessionTickets.Remove(session);
            await db.SaveChangesAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.Value);
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISessionTicketDbContext>();

        var session = await db.SessionTickets.FirstOrDefaultAsync(s => s.Key == key);
        if (session is not null)
        {
            db.SessionTickets.Remove(session);
            await db.SaveChangesAsync();
        }
    }
}
