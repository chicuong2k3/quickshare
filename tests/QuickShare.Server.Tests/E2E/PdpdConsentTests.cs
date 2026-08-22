using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Features.Users.Consent;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// PDPD consent (#230).
///
/// Decree 13 wants consent that is specific and withdrawable, and "specific" is only provable if
/// the record names the text that was shown. So the version is the load-bearing part here: an
/// agreement to last year's wording is not an agreement to this year's, and the tests say so.
///
/// The wording itself is a legal question, written down in `docs/pdpd-compliance.md` for review.
/// What is tested is the mechanism.
/// </summary>
public sealed class PdpdConsentTests : IClassFixture<TestWebAppFactory>
{
    private const string Consents = "/api/consents/pdpd";

    private readonly TestWebAppFactory _factory;

    public PdpdConsentTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Consent_StartsUngranted()
    {
        var (client, _) = await AccountAsync();

        var status = await StatusAsync(client);

        Assert.False(status.Granted);
        Assert.Equal(ConsentKinds.PdpdVersion, status.Version);
        Assert.Null(status.GrantedAt);
    }

    [Fact]
    public async Task Granting_RecordsTheVersionAndTheMoment()
    {
        var (client, userId) = await AccountAsync();

        (await client.PostAsync(Consents, null)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.UserConsents.AsNoTracking().SingleAsync(c => c.UserId == userId);

        Assert.Equal(ConsentKinds.Pdpd, record.Kind);
        Assert.Equal(ConsentKinds.PdpdVersion, record.Version);
        // Hashed, like every other address recorded anywhere here.
        Assert.NotEqual(string.Empty, record.IpHash);
    }

    [Fact]
    public async Task GrantingTwice_DoesNotWriteASecondRow()
    {
        var (client, userId) = await AccountAsync();

        (await client.PostAsync(Consents, null)).EnsureSuccessStatusCode();
        (await client.PostAsync(Consents, null)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A log with an entry per page load says nothing about when somebody actually agreed.
        Assert.Equal(1, await db.UserConsents.CountAsync(c => c.UserId == userId));
    }

    [Fact]
    public async Task Withdrawing_MarksTheRecordRatherThanErasingIt()
    {
        var (client, userId) = await AccountAsync();
        (await client.PostAsync(Consents, null)).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(Consents);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.False((await StatusAsync(client)).Granted);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.UserConsents.AsNoTracking().SingleAsync(c => c.UserId == userId);

        // "Consented on Tuesday, withdrew on Friday" is the record that matters; deleting it
        // would erase the half that protects the person.
        Assert.NotNull(record.RevokedAt);
    }

    [Fact]
    public async Task AConsentToOlderWording_DoesNotCountForTheCurrentText()
    {
        var (client, userId) = await AccountAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserConsents.Add(new QuickShare.Data.Entities.UserConsent
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Kind = ConsentKinds.Pdpd,
                Version = "2020-old-wording",
                GrantedAt = DateTime.UtcNow.AddYears(-1),
                IpHash = "hash",
            });
            await db.SaveChangesAsync();
        }

        // The whole point of versioning: agreeing to last year's text is not agreeing to this
        // year's.
        Assert.False((await StatusAsync(client)).Granted);
    }

    [Fact]
    public async Task Consent_IsPerAccount()
    {
        var (mine, _) = await AccountAsync();
        var (theirs, _) = await AccountAsync();

        (await mine.PostAsync(Consents, null)).EnsureSuccessStatusCode();

        Assert.True((await StatusAsync(mine)).Granted);
        Assert.False((await StatusAsync(theirs)).Granted);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Free);

        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<ConsentStatusDto> StatusAsync(HttpClient client)
    {
        var response = await client.GetAsync(Consents);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ConsentStatusDto>())!;
    }
}
