using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Users.Devices;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class DeviceRemovalFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeviceRemovalFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Form_RequiresAntiforgeryAndOnlyRemovesOwnersDevice()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        var otherKey = Guid.CreateVersion7().ToString();
        var other = await _factory.SeedUserAsync(otherKey);
        Guid deviceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracker = new DeviceTrackerService();
            var (device, _) = await tracker.RecordLoginAsync(db, owner.Id, "device-test", "203.0.113.8", CancellationToken.None);
            await tracker.RecordLoginAsync(db, other.Id, "device-test", "203.0.113.9", CancellationToken.None);
            deviceId = device.Id;
        }

        using (var otherClient = Client(otherKey))
        {
            var html = await otherClient.GetStringAsync(RoutePaths.AccountDevices);
            using var form = Form(deviceId, RoutePaths.AccountDevices, AntiforgeryHelper.ExtractToken(html));
            using var response = await otherClient.PostAsync(RoutePaths.AccountDeviceRemove, form);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var ownerClient = Client(ownerKey))
        {
            var html = await ownerClient.GetStringAsync(RoutePaths.AccountDevices);
            Assert.Contains($"value=\"{deviceId}\"", html);
            using (var missingToken = Form(deviceId, RoutePaths.AccountDevices, null))
            {
                using var rejected = await ownerClient.PostAsync(RoutePaths.AccountDeviceRemove, missingToken);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }

            using var form = Form(deviceId, "https://example.com", AntiforgeryHelper.ExtractToken(html));
            using var response = await ownerClient.PostAsync(RoutePaths.AccountDeviceRemove, form);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
            Assert.Equal(RoutePaths.Account, response.Headers.Location?.OriginalString);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.UserDevices.AnyAsync(d => d.Id == deviceId));
        Assert.True(await verifyDb.SecurityEvents.AnyAsync(e => e.UserId == owner.Id && e.EventType == SecurityEventType.SessionRevoked));
    }

    private HttpClient Client(string key)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");
        return client;
    }

    private static FormUrlEncodedContent Form(Guid deviceId, string returnTo, string? antiforgery)
    {
        var fields = new Dictionary<string, string>
        {
            [RemoveDeviceFormEndpoint.DeviceIdField] = deviceId.ToString(),
            [RemoveDeviceFormEndpoint.ReturnToField] = returnTo,
        };
        if (antiforgery is not null)
        {
            fields["__RequestVerificationToken"] = antiforgery;
        }
        return new FormUrlEncodedContent(fields);
    }
}
