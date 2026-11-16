using Microsoft.AspNetCore.DataProtection;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// Signed, URL-safe unsubscribe tokens. Reuses the app's DataProtection keyring —
/// no extra secret to configure or rotate.
/// </summary>
public sealed class UnsubscribeTokens(IDataProtectionProvider provider)
{
    private const string Purpose = "quickshare.unsubscribe.download-notify";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Create(Guid userId) => _protector.Protect(userId.ToString());

    public bool TryRead(string token, out Guid userId)
    {
        userId = Guid.Empty;

        try
        {
            return Guid.TryParse(_protector.Unprotect(token), out userId);
        }
        catch
        {
            return false;
        }
    }
}
