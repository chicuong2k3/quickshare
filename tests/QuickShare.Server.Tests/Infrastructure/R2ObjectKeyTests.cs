using QuickShare.Server.Infrastructure.Storage;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class R2ObjectKeyTests
{
    private static readonly Guid UserId = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid DeliveryId = Guid.Parse("22222222222222222222222222222222");

    [Fact]
    public void For_standard_inputs_returns_correct_key_format()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "photo.jpg");
        Assert.Equal("uploads/11111111111111111111111111111111/22222222222222222222222222222222/photo.jpg", key);
    }

    [Fact]
    public void For_escapes_spaces_in_filename()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "my photo.jpg");
        Assert.DoesNotContain(" ", key);
        Assert.Contains("my%20photo.jpg", key);
    }

    [Fact]
    public void For_escapes_forward_slashes_in_filename()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "subdir/file.pdf");
        Assert.DoesNotContain("/file.pdf", key);
    }

    [Fact]
    public void For_escapes_backslashes_in_filename()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "folder\\file.txt");
        Assert.DoesNotContain("\\", key);
    }

    [Fact]
    public void For_handles_empty_filename()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "");
        Assert.EndsWith("/", key);
    }

    [Fact]
    public void For_handles_special_characters()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "file#1@2&3.txt");
        Assert.Contains("file%231%402%263.txt", key);
    }

    [Fact]
    public void For_always_uses_lowercase_hex_for_guids()
    {
        var key = R2ObjectKey.For(UserId, DeliveryId, "doc.pdf");
        var parts = key.Split('/');
        Assert.Matches("^[0-9a-f]{32}$", parts[1]);
        Assert.Matches("^[0-9a-f]{32}$", parts[2]);
    }
}
