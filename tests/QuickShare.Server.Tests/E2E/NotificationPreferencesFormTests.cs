using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Features.Users.Notifications;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class NotificationPreferencesFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public NotificationPreferencesFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NativeForm_SavesPreferencesForCurrentUser()
    {
        var userKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(userKey);
        using var client = _factory.CreateAuthenticatedClient(userKey);
        var html = await client.GetStringAsync(RoutePaths.AccountNotifications);
        var token = AntiforgeryHelper.ExtractToken(html);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            [UpdateNotificationPreferencesFormEndpoint.TrackMyDownloadsField] = "on",
        });

        using var response = await client.PostAsync(UpdateNotificationPreferencesFormEndpoint.FormRoute, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.False(saved.EmailOnDownload);
        Assert.True(saved.TrackMyDownloads);
    }

    [Fact]
    public async Task NativeForm_RejectsMissingAntiforgeryToken()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [UpdateNotificationPreferencesFormEndpoint.TrackMyDownloadsField] = "on",
        });

        using var response = await client.PostAsync(UpdateNotificationPreferencesFormEndpoint.FormRoute, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
