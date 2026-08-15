using Microsoft.AspNetCore.DataProtection;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// Signed, URL-safe tokens for anonymous-sender email actions (confirm / unsubscribe).
/// Reuses the app's DataProtection keyring — no extra secret to configure or rotate.
/// Distinct purposes keep confirm and unsubscribe tokens from interoperating.
/// </summary>
public sealed class SenderEmailTokens(IDataProtectionProvider provider)
{
    private const string ConfirmPurpose = "quickshare.sender-email.confirm";
    private const string UnsubscribePurpose = "quickshare.sender-email.unsubscribe";

    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly IDataProtector _confirm = provider.CreateProtector(ConfirmPurpose);
    private readonly IDataProtector _unsubscribe = provider.CreateProtector(UnsubscribePurpose);

    public string CreateConfirmToken(Guid deliveryId, DateTimeOffset? expiresAt = null)
        => Create(_confirm, deliveryId, expiresAt);

    public bool TryReadConfirmToken(string token, out Guid deliveryId, out DateTimeOffset expiresAt)
        => TryRead(_confirm, token, out deliveryId, out expiresAt);

    public string CreateUnsubscribeToken(Guid deliveryId, DateTimeOffset? expiresAt = null)
        => Create(_unsubscribe, deliveryId, expiresAt);

    public bool TryReadUnsubscribeToken(string token, out Guid deliveryId, out DateTimeOffset expiresAt)
        => TryRead(_unsubscribe, token, out deliveryId, out expiresAt);

    private static string Create(IDataProtector protector, Guid deliveryId, DateTimeOffset? expiresAt)
    {
        var expiry = (expiresAt ?? DateTimeOffset.UtcNow.Add(DefaultExpiry)).ToUnixTimeSeconds();
        return protector.Protect($"{deliveryId:N}|{expiry}");
    }

    private static bool TryRead(IDataProtector protector, string token, out Guid deliveryId, out DateTimeOffset expiresAt)
    {
        deliveryId = Guid.Empty;
        expiresAt = default;

        try
        {
            var parts = protector.Unprotect(token).Split('|');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out deliveryId))
                return false;

            if (!long.TryParse(parts[1], out var unix))
                return false;

            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            return expiresAt > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
        }
    }
}
