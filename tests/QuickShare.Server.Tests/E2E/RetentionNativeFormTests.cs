using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class RetentionNativeFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public RetentionNativeFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PreviewAndSave_UseSelectedWindowAndOnlySaveOnRequest()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        using var client = _factory.CreateAuthenticatedClient(key);
        var html = await client.GetStringAsync(RoutePaths.AccountSecurityActivity);
        var token = AntiforgeryHelper.ExtractToken(html);
        using var preview = Form(token, "preview");
        using var previewResponse = await client.PostAsync(RoutePaths.AccountSecurityActivity, preview);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).RetentionDays);

        using var save = Form(token, "save");
        using var saveResponse = await client.PostAsync(RoutePaths.AccountSecurityActivity, save);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(30, saved.RetentionDays);
        Assert.True(saved.RetentionNoExtend);
    }

    [Fact]
    public async Task Save_RejectsMissingAntiforgeryToken()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        using var client = _factory.CreateAuthenticatedClient(key);
        using var form = Form(string.Empty, "save");

        using var response = await client.PostAsync(RoutePaths.AccountSecurityActivity, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).RetentionDays);
    }

    [Fact]
    public async Task Save_FreeAccountCannotChangeRetention()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);
        var page = await client.GetStringAsync(RoutePaths.AccountSecurityActivity);
        using var form = Form(AntiforgeryHelper.ExtractToken(page), "save");

        using var response = await client.PostAsync(RoutePaths.AccountSecurityActivity, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).RetentionDays);
    }

    private static FormUrlEncodedContent Form(string token, string action) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["_handler"] = "retention-policy",
        ["retentionDays"] = "30",
        ["retentionNoExtend"] = "on",
        ["action"] = action,
    });
}
