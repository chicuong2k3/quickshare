namespace QuickShare.Infrastructure.Storage;

public static class R2ObjectKey
{
    /// <summary>All upload keys live under this prefix — the orphan sweep never leaves it.</summary>
    public const string UploadsPrefix = "uploads/";

    public static string For(Guid userId, Guid deliveryId, string fileName)
    {
        var safeName = Uri.EscapeDataString(fileName);
        return $"{UploadsPrefix}{userId:N}/{deliveryId:N}/{safeName}";
    }
}
