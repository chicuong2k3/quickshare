using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuickShare.Data;

/// <summary>Design-time only: lets `dotnet ef migrations add` build the model without running the full host (Program.cs).</summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=quickshare_design;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options);
    }
}
