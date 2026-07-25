using System.Text;
using System.Web;

namespace QuickShare.Server.Infrastructure.Storage;

public static class R2ObjectKey
{
    public static string For(Guid userId, Guid deliveryId, string fileName)
    {
        var safeName = Uri.EscapeDataString(fileName);
        return $"uploads/{userId:N}/{deliveryId:N}/{safeName}";
    }
}
