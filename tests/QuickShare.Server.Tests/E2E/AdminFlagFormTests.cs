using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Features.Admin.Flags;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Server.Tests.E2E;

public sealed class AdminFlagFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AdminFlagFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NativeForm_AdminCanChangeFlag()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var html = await client.GetStringAsync(RoutePaths.AdminFlags);
        Assert.Contains(FeatureFlagKeys.Previews, html);
        var token = AntiforgeryHelper.ExtractToken(html);
        using var form = Form(token, FeatureFlagKeys.Previews, "37", true);

        using var response = await client.PostAsync(UpdateFlagFormEndpoint.FormRoute, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
        var flag = (await service.ListAsync()).Single(f => f.Key == FeatureFlagKeys.Previews);
        Assert.True(flag.EffectiveEnabled);
        Assert.Equal(37, flag.EffectiveRolloutPercent);
        await service.SetOverrideAsync(FeatureFlagKeys.Previews, false, 100);
    }

    [Fact]
    public async Task NativeForm_RejectsMissingTokenAndInvalidValues()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var missingToken = Form(string.Empty, FeatureFlagKeys.Previews, "37", true);
        using var rejected = await client.PostAsync(UpdateFlagFormEndpoint.FormRoute, missingToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var token = AntiforgeryHelper.ExtractToken(await client.GetStringAsync(RoutePaths.AdminFlags));
        using var invalidRollout = Form(token, FeatureFlagKeys.Previews, "101", true);
        using var invalidResponse = await client.PostAsync(UpdateFlagFormEndpoint.FormRoute, invalidRollout);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var unknownFlag = Form(token, "unknown-flag", "50", true);
        using var unknownResponse = await client.PostAsync(UpdateFlagFormEndpoint.FormRoute, unknownFlag);
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
    }

    [Fact]
    public async Task NativeForm_RejectsNonAdmin()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, "nobody@example.com");
        using var form = Form(string.Empty, FeatureFlagKeys.Previews, "50", true);

        using var response = await client.PostAsync(UpdateFlagFormEndpoint.FormRoute, form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static FormUrlEncodedContent Form(string token, string key, string rollout, bool enabled)
    {
        var values = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            [UpdateFlagFormEndpoint.KeyField] = key,
            [UpdateFlagFormEndpoint.RolloutField] = rollout,
        };
        if (enabled)
        {
            values[UpdateFlagFormEndpoint.EnabledField] = "true";
        }

        return new FormUrlEncodedContent(values);
    }
}
