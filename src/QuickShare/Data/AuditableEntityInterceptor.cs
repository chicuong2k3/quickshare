using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuickShare.Data;

public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var db = eventData.Context;
        if (db is null)
            return new ValueTask<InterceptionResult<int>>(result);

        var now = DateTime.UtcNow;
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                var createdAtProp = entry.Metadata.FindProperty("CreatedAt");
                if (createdAtProp is not null && createdAtProp.ClrType == typeof(DateTime))
                    entry.Property("CreatedAt").CurrentValue = now;

                var updatedAtProp = entry.Metadata.FindProperty("UpdatedAt");
                if (updatedAtProp is not null && updatedAtProp.ClrType == typeof(DateTime?))
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                var updatedAtProp = entry.Metadata.FindProperty("UpdatedAt");
                if (updatedAtProp is not null && updatedAtProp.ClrType == typeof(DateTime?))
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
        }

        return new ValueTask<InterceptionResult<int>>(result);
    }
}
