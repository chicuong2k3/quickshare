using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace QuickShare.Infrastructure.Push;

public sealed class PushOptions
{
    public const string Section = "Push";

    public string VapidSubject { get; init; } = string.Empty;
    public string VapidPublicKey { get; init; } = string.Empty;
    public string VapidPrivateKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);
}

public interface IPushService
{
    string PublicKey { get; }
    bool IsConfigured { get; }
    Task SendAsync(
        IReadOnlyList<Data.Entities.PushSubscription> subscriptions,
        string title, string message, string link, CancellationToken ct);
}

/// <summary>
/// Sends web-push notifications (issue #103). Best-effort: a dead/expired subscription
/// is dropped rather than breaking the batch; a failed push never fails the caller.
/// </summary>
public sealed class PushService : IPushService
{
    private readonly PushServiceClient _client;
    private readonly PushOptions _options;

    public PushService(IOptions<PushOptions> options)
    {
        _options = options.Value;
        _client = new PushServiceClient();
        if (_options.IsConfigured)
        {
            _client.DefaultAuthentication = new VapidAuthentication(_options.VapidPublicKey, _options.VapidPrivateKey)
            {
                Subject = _options.VapidSubject,
            };
        }
    }

    public string PublicKey => _options.VapidPublicKey;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(
        IReadOnlyList<Data.Entities.PushSubscription> subscriptions,
        string title, string message, string link, CancellationToken ct)
    {
        if (!IsConfigured || subscriptions.Count == 0)
            return;

        var payload = JsonSerializer.Serialize(new PushPayload(title, message, link));

        foreach (var sub in subscriptions)
        {
            try
            {
                var webSubscription = new WebPushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        { "p256dh", sub.P256dh },
                        { "auth", sub.Auth },
                    },
                };
                await _client.RequestPushMessageDeliveryAsync(webSubscription, new PushMessage(payload), ct);
            }
            catch (Exception)
            {
                // Best-effort: an expired endpoint (410) or network hiccup is dropped.
            }
        }
    }

    private sealed record PushPayload(string Title, string Message, string Link);
}
