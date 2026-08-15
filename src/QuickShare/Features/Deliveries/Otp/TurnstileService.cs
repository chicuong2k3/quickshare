using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace QuickShare.Features.Deliveries.Otp;

/// <summary>
/// Cloudflare Turnstile configuration (issue #175). When SiteKey + SecretKey
/// are unset, the service is disabled and every challenge passes — so the
/// feature is opt-in and the app works without a Cloudflare account.
/// </summary>
public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    public string SiteKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>Failed OTP attempts before a Turnstile challenge is required.</summary>
    public int ChallengeAfterFailures { get; init; } = 3;

    public bool IsConfigured => !string.IsNullOrEmpty(SiteKey) && !string.IsNullOrEmpty(SecretKey);
}

/// <summary>
/// Server-side Turnstile token verification (issue #175). Always returns true
/// when the feature is not configured, so production without Turnstile keys
/// behaves exactly as before.
/// </summary>
public sealed class TurnstileService(HttpClient httpClient, IOptions<TurnstileOptions> options)
{
    private const string VerifyEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly TurnstileOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return true;

        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token
            };
            if (!string.IsNullOrEmpty(remoteIp))
                payload["remoteip"] = remoteIp;

            var response = await httpClient.PostAsJsonAsync(VerifyEndpoint, payload, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(cancellationToken: ct);
            return result?.Success == true;
        }
        catch
        {
            // Fail closed on network errors: the challenge cannot be proven.
            return false;
        }
    }

    private sealed class TurnstileVerifyResponse
    {
        public bool Success { get; set; }
    }
}
