using Microsoft.EntityFrameworkCore;
using QuickShare.Shared.Infrastructure;
using QuickShare.Shared.Models;

namespace QuickShare.Web.Data;

public sealed class TicketDbContext : DbContext, ISessionTicketDbContext
{
    public TicketDbContext(DbContextOptions<TicketDbContext> options) : base(options) { }

    public DbSet<SessionTicket> SessionTickets => Set<SessionTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionTicket>(entity =>
        {
            entity.ToTable("session_tickets");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.SchemeName).IsRequired().HasMaxLength(64);
        });
    }
}
