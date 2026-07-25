using Microsoft.EntityFrameworkCore;

namespace QuickShare.Shared.Infrastructure;

public interface ISessionTicketDbContext
{
    DbSet<Models.SessionTicket> SessionTickets { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
