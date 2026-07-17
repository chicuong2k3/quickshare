using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TeleCloud.Server.Data;
using TeleCloud.Server.Infrastructure;

namespace TeleCloud.Server.Tests.TempShares;

/// <summary>
/// A fake <see cref="ITempShareMessageDeleter"/> that records every message id it is asked to delete
/// and can be configured to succeed or fail per call. Avoids needing a live WTelegramClient.
/// </summary>
internal sealed class FakeMessageDeleter : ITempShareMessageDeleter
{
    public List<int> DeletedMessageIds { get; } = [];

    public Func<int, bool>? ShouldSucceed { get; set; }

    public Task<bool> DeleteAsync(int messageId, CancellationToken ct = default)
    {
        var ok = ShouldSucceed?.Invoke(messageId) ?? true;
        if (ok)
            DeletedMessageIds.Add(messageId);
        return Task.FromResult(ok);
    }
}

public class TempShareCleanupServiceTests
{
    private static TeleCloudDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TeleCloudDbContext>()
            .UseInMemoryDatabase($"tempshare-cleanup-{Guid.NewGuid():N}")
            .Options;
        var db = new TeleCloudDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    // The cleanup service filters against the real DateTime.UtcNow, so seed rows
    // relative to it rather than a fixed timestamp.
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Cleanup_RemovesExpiredFiles_AndPassesMessageIdsToDeleter()
    {
        await using var db = NewContext();
        var deleter = new FakeMessageDeleter();

        var expiredFile = NewFile("expired.pdf", messageId: 100);
        var liveFile = NewFile("live.pdf", messageId: 200);

        db.Files.AddRange(expiredFile, liveFile);
        db.TempShares.AddRange(
            NewShare(expiredFile.Id, Now.AddHours(-1)),
            NewShare(liveFile.Id, Now.AddHours(12)));
        await db.SaveChangesAsync();

        await TempShareCleanupService.CleanupExpiredAsync(db, deleter, NullLogger<TempShareCleanupService>.Instance, default);

        // expired file removed, live file kept
        Assert.False(await db.Files.AnyAsync(f => f.Id == expiredFile.Id));
        Assert.True(await db.Files.AnyAsync(f => f.Id == liveFile.Id));

        // only the expired message id was passed to the deleter
        Assert.Equal([100], deleter.DeletedMessageIds);
    }

    [Fact]
    public async Task Cleanup_KeepsRowsWhenDeleterFails_SoTheyRetryNextTick()
    {
        await using var db = NewContext();
        var deleter = new FakeMessageDeleter { ShouldSucceed = _ => false };

        var expiredFile = NewFile("expired.pdf", messageId: 999);
        db.Files.Add(expiredFile);
        db.TempShares.Add(NewShare(expiredFile.Id, Now.AddHours(-1)));
        await db.SaveChangesAsync();

        await TempShareCleanupService.CleanupExpiredAsync(db, deleter, NullLogger<TempShareCleanupService>.Instance, default);

        // Telegram delete failed → keep the file row so the next tick can retry
        Assert.True(await db.Files.AnyAsync(f => f.Id == expiredFile.Id));
        Assert.Empty(deleter.DeletedMessageIds);
    }

    [Fact]
    public async Task Cleanup_WithNoExpiredRows_DoesNothing()
    {
        await using var db = NewContext();
        var deleter = new FakeMessageDeleter();

        var liveFile = NewFile("live.pdf", messageId: 555);
        db.Files.Add(liveFile);
        db.TempShares.Add(NewShare(liveFile.Id, Now.AddHours(24)));
        await db.SaveChangesAsync();

        await TempShareCleanupService.CleanupExpiredAsync(db, deleter, NullLogger<TempShareCleanupService>.Instance, default);

        Assert.True(await db.Files.AnyAsync(f => f.Id == liveFile.Id));
        Assert.Empty(deleter.DeletedMessageIds);
    }

    private static FileMetadata NewFile(string filename, int messageId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Filename = filename,
            ContentType = "application/octet-stream",
            Size = 1024,
            ChannelId = 1,
            MessageId = messageId,
            AccessHash = 1,
            FileReference = [],
            DocumentId = 1,
            FolderId = Guid.NewGuid(),
            Tags = [],
            CreatedAt = Now
        };

    private static TempShare NewShare(Guid fileId, DateTime expiresAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FileId = fileId,
            ExpiresAt = expiresAt,
            CreatedAt = Now
        };
}
