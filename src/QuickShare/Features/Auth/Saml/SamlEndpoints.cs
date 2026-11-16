using System.Security.Claims;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuickShare.Features.Auth.Sso;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Auth.Saml;

/// <summary>
/// The SAML 2.0 endpoints (issue #139).
///
/// Written as minimal-API routes rather than FastEndpoints because all three speak in redirects
/// and form posts to a machine, not JSON to a caller: the ACS reads a form field an identity
/// provider posted from another origin, and the login route's only output is a 302.
/// </summary>
public static class SamlEndpoints
{
    /// <summary>Sent to the identity provider and echoed back, so a login lands where it started.</summary>
    private const string RelayStateReturnUrl = "ReturnUrl";
    private const int MaxRelayStateLength = 512;

    public static void MapSamlEndpoints(this WebApplication app)
    {
        // ── SP metadata ──
        // Anonymous and cacheable by design: it is public configuration, and an IdP admin has to
        // be able to fetch it before any binding exists.
        app.MapGet(SamlRoutes.Metadata, (SamlService saml) =>
            Results.Content(saml.BuildMetadata(), "application/samlmetadata+xml"));

        // ── SP-initiated login ──
        app.MapGet($"{SamlRoutes.Login}/{{workspaceId:guid}}", async (
            Guid workspaceId, string? returnUrl, HttpContext context, SamlService saml, CancellationToken ct) =>
        {
            var binding = await saml.FindByWorkspaceAsync(workspaceId, ct);
            if (binding is null || !binding.IsSamlUsable)
                return Results.NotFound();

            var config = saml.BuildConfiguration(binding);
            var request = new Saml2AuthnRequest(config)
            {
                Subject = null,
                NameIdPolicy = new NameIdPolicy
                {
                    AllowCreate = true,
                    Format = NameIdentifierFormats.Email.OriginalString,
                },
            };

            var redirect = new Saml2RedirectBinding();
            // Only a local path is ever echoed back: RelayState is attacker-influenced data that
            // comes back to us as a redirect target, which is an open-redirect if taken on trust.
            redirect.SetRelayStateQuery(new Dictionary<string, string>
            {
                { RelayStateReturnUrl, SafeReturnUrl(returnUrl) },
            });

            // The bound redirect location is the whole output — taking the Uri directly avoids
            // dragging an MVC IActionResult through a minimal-API route for a 302.
            var bound = redirect.Bind(request);
            return Results.Redirect(bound.RedirectLocation.OriginalString);
        });

        // ── Assertion consumer ──
        // Serves both SP-initiated and IdP-initiated sign-in: an IdP-initiated assertion arrives
        // here with no prior request, which is exactly why the issuer has to identify the binding.
        app.MapPost(SamlRoutes.Acs, async (
            HttpContext context,
            SamlService saml,
            SsoLoginService sso,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var httpRequest = context.Request.ToGenericHttpRequest(validate: true);

            // Which certificate must this assertion be signed by? Only the issuer can say, and
            // the issuer is inside the assertion — so it has to be read before anything about it
            // has been proved. That read is done here, deliberately, as raw untrusted XML rather
            // than through the token handler: a handler read looks like validation and is not one,
            // and the next person to touch this file must not be able to mistake it for one.
            //
            // Nothing below acts on this value except to select a binding. Every claim that
            // matters is re-read from the response the library has validated.
            var untrustedIssuer = SamlService.TryReadIssuer(context.Request.Form["SAMLResponse"]);
            if (string.IsNullOrEmpty(untrustedIssuer))
            {
                saml.LogRejected("no issuer", null);
                return Results.BadRequest();
            }

            var binding = await saml.FindByIssuerAsync(untrustedIssuer, ct);
            if (binding is null || !binding.IsSamlUsable)
            {
                saml.LogRejected("no workspace binding", untrustedIssuer);
                return Results.BadRequest();
            }

            var issuer = untrustedIssuer;

            var config = saml.BuildConfiguration(binding);
            var response = new Saml2AuthnResponse(config);

            try
            {
                httpRequest.Binding.ReadSamlResponse(httpRequest, response);

                if (response.Status != Saml2StatusCodes.Success)
                {
                    saml.LogRejected($"status {response.Status}", issuer);
                    return Results.BadRequest();
                }

                // Signature, audience, issuer and validity window are all checked here. Every
                // decision below this line rests on it, and nothing above it may be acted on.
                httpRequest.Binding.Unbind(httpRequest, response);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                saml.LogRejected($"validation failed: {ex.Message}", issuer);
                return Results.BadRequest();
            }

            var identity = response.ClaimsIdentity;
            if (identity is null)
            {
                saml.LogRejected("no assertion identity", issuer);
                return Results.BadRequest();
            }

            // A correctly signed assertion replayed is still an attack: the library checks that
            // the signature is good and the window is open, neither of which notices a second
            // arrival of the same assertion.
            var assertionId = response.Id?.Value ?? string.Empty;
            if (await saml.IsReplayAsync(assertionId, ct))
            {
                saml.LogRejected("replayed assertion", issuer);
                return Results.BadRequest();
            }

            var email = SamlService.ReadEmail(identity);
            if (string.IsNullOrEmpty(email))
            {
                saml.LogRejected("assertion carries no email", issuer);
                return Results.BadRequest();
            }

            // The narrowing filter, applied after the signature already proved the directory —
            // the same order the OIDC path uses, for the same reason (#181).
            if (!SsoLoginService.EmailMatchesDirectory(binding, email))
            {
                saml.LogRejected("email outside allowed domains", issuer);
                return Results.BadRequest();
            }

            var displayName = identity.FindFirst(ClaimTypes.Name)?.Value ?? email;
            var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? email;

            var analytics = context.RequestServices.GetRequiredService<AnalyticsService>();
            var profile = await UserRegistrationService.RegisterAsync(
                SsoProvider.Saml, subject, displayName, email, null, db, ct, analytics, null);

            // Group claims are re-shaped into the same claim the OIDC path uses, so one
            // provisioning implementation serves both rather than two that drift apart.
            var provisioningClaims = SamlService.ReadGroups(identity)
                .Select(g => new Claim(SsoLoginService.GroupsClaim, g))
                .ToList();

            await sso.ApplySamlAsync(profile.Id, email, binding, provisioningClaims, ct);

            var sessions = context.RequestServices.GetRequiredService<Users.Sessions.SessionService>();
            var session = await sessions.StartAsync(profile.Id, context, ct);

            await SignInAsync(context, profile.Id, subject, displayName, email, session.SessionId);

            await SecurityEventService.AppendAsync(db, profile.Id, SecurityEventType.Login,
                context.Request.Headers.UserAgent.ToString(),
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

            var relayState = ReadRelayState(httpRequest);
            return Results.Redirect(relayState);
        }).WithMetadata(SkipAntiforgeryValidation.Instance);
    }

    /// <summary>
    /// Issues the auth cookie. The claims match what the OAuth handlers produce, because
    /// everything downstream — <c>GetUserAsync</c>, the MFA gate, the workspace lookups — reads
    /// those two claims and must not care which front door was used.
    /// </summary>
    private static Task SignInAsync(
        HttpContext context, Guid userId, string subject, string name, string email, string sessionId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(Users.Sessions.SessionService.SessionClaimType, sessionId),
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(AuthConstants.ProviderClaimType, SsoProvider.Saml),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Email, email),
            // Federated sign-in already carried a second factor if the directory required one;
            // #219's own exemption says the same thing, so the session starts verified.
            new Claim(Totp.MfaSession.VerifiedClaimType, "1"),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        _ = userId;
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static string ReadRelayState(ITfoxtec.Identity.Saml2.Http.HttpRequest httpRequest)
    {
        try
        {
            var relay = new Saml2RedirectBinding();
            relay.SetRelayStateQuery([]);
            var state = httpRequest.Binding?.GetRelayStateQuery();
            return state is not null && state.TryGetValue(RelayStateReturnUrl, out var url)
                ? SafeReturnUrl(url)
                : "/";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing or malformed RelayState is normal for IdP-initiated sign-in.
            return "/";
        }
    }

    /// <summary>
    /// RelayState round-trips through the identity provider, so it is attacker-influenceable data
    /// that comes back as a redirect target. Only a local path is ever honoured — anything else
    /// would make this an open redirect wearing an SSO badge.
    /// </summary>
    private static string SafeReturnUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxRelayStateLength)
            return "/";

        // "//evil.example" is a protocol-relative absolute URL, and reads as a path if you only
        // check the first character.
        return candidate.StartsWith('/') && !candidate.StartsWith("//", StringComparison.Ordinal)
            ? candidate
            : "/";
    }
}
