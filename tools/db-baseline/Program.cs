using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuickShare.Data;

// Usage: dotnet run --project tools/db-baseline [connectionString]
// Defaults to the local dev Postgres used by QuickShare's user-secrets.

const string DefaultConnection =
    "Host=localhost;Port=5432;Database=quickshare;Username=postgres;SSL Mode=Disable";

var connectionString = args.Length > 0 ? args[0] : DefaultConnection;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new AppDbContext(options);

Console.WriteLine("Building schema from the current EF model (EnsureCreated)...");
await db.Database.EnsureCreatedAsync();

var migrationsAssembly = db.GetService<IMigrationsAssembly>();
var migrationIds = migrationsAssembly.Migrations.Keys
    .OrderBy(id => id, StringComparer.Ordinal)
    .ToList();

Console.WriteLine($"Stamping {migrationIds.Count} migrations into __EFMigrationsHistory...");

await db.Database.ExecuteSqlRawAsync(
    "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" "
    + "(\"MigrationId\" character varying(150) NOT NULL, "
    + "\"ProductVersion\" character varying(32) NOT NULL, "
    + "CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY (\"MigrationId\"))");

foreach (var migrationId in migrationIds)
{
    await db.Database.ExecuteSqlRawAsync(
        "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") "
        + "VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
        migrationId,
        "10.0.0");
}

var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
Console.WriteLine($"Pending migrations after stamp: {pending.Count}");

foreach (var id in pending)
{
    Console.WriteLine($"  pending: {id}");
}

Console.WriteLine(pending.Count == 0
    ? "Done. Startup will skip MigrateAsync."
    : "WARNING: model and migration list disagree — startup may still migrate.");
