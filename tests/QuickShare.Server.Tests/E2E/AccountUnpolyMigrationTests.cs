using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class AccountUnpolyMigrationTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AccountUnpolyMigrationTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Account_DirectGet_RendersCardsWithoutJavascript()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(RoutePaths.Account);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"account-app\"", html);
        Assert.Contains("id=\"profile-card\"", html);
        Assert.Contains(TestWebAppFactory.TestUserName, html);
        Assert.Contains("id=\"usage-card\"", html);
        Assert.Contains("id=\"onboarding-card\"", html);
        Assert.Contains("id=\"devices-card\"", html);
        Assert.Contains("id=\"referral-card\"", html);
        Assert.Contains("id=\"downloads-card\"", html);
    }

    [Fact]
    public async Task Invoices_DirectAndTargetedGet_RenderOwnerOnly()
    {
        const string company = "Owner Billing Company";
        const string invoiceNumber = "INV-TEST-2026";
        var ownerKey = Guid.CreateVersion7().ToString();
        var otherKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(otherKey);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BillingProfiles.Add(new BillingProfile
            {
                UserId = owner.Id,
                CompanyName = company,
            });
            db.Invoices.Add(new Invoice
            {
                UserId = owner.Id,
                Number = invoiceNumber,
                Status = InvoiceStatus.Issued,
                PeriodStart = new DateTime(2026, 1, 1),
                PeriodEnd = new DateTime(2026, 2, 1),
            });
            await db.SaveChangesAsync();
        }

        using var ownerClient = _factory.CreateAuthenticatedClient(ownerKey);
        var response = await ownerClient.GetAsync(RoutePaths.AccountInvoices);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"invoices-body\"", html);
        Assert.Contains($"value=\"{company}\"", html);
        Assert.Contains(invoiceNumber, html);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountInvoices);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#invoices-body");
        var targeted = await ownerClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains(invoiceNumber, await targeted.Content.ReadAsStringAsync());

        using var otherClient = _factory.CreateAuthenticatedClient(otherKey);
        var other = await otherClient.GetStringAsync(RoutePaths.AccountInvoices);
        Assert.DoesNotContain(company, other);
        Assert.DoesNotContain(invoiceNumber, other);
    }

    [Fact]
    public async Task Recurring_DirectAndTargetedGet_KeepPlanAndOwnerBoundaries()
    {
        const string ownerSchedule = "Owner weekly delivery";
        const string otherSchedule = "Other weekly delivery";
        var ownerKey = Guid.CreateVersion7().ToString();
        var otherKey = Guid.CreateVersion7().ToString();
        var freeKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Pro);
        var other = await _factory.SeedUserAsync(otherKey, PlanTierLimits.Pro);
        await _factory.SeedUserAsync(freeKey);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerSource = new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = owner.Id,
                FileName = "owner-source.pdf",
                ShareCode = Guid.CreateVersion7().ToString("N"),
            };
            var otherSource = new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = other.Id,
                FileName = "other-source.pdf",
                ShareCode = Guid.CreateVersion7().ToString("N"),
            };
            db.Deliveries.AddRange(ownerSource, otherSource);
            db.RecurringDeliveries.Add(new RecurringDelivery
            {
                OwnerId = owner.Id,
                SourceDeliveryId = ownerSource.Id,
                Name = ownerSchedule,
                NextRunAt = DateTime.UtcNow.AddDays(1),
            });
            db.RecurringDeliveries.Add(new RecurringDelivery
            {
                OwnerId = other.Id,
                SourceDeliveryId = otherSource.Id,
                Name = otherSchedule,
                NextRunAt = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        using var ownerClient = _factory.CreateAuthenticatedClient(ownerKey);
        var direct = await ownerClient.GetStringAsync(RoutePaths.AccountRecurring);
        Assert.Contains("id=\"recurring-body\"", direct);
        Assert.Contains(ownerSchedule, direct);
        Assert.DoesNotContain(otherSchedule, direct);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountRecurring);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#recurring-body");
        var targeted = await ownerClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains(ownerSchedule, await targeted.Content.ReadAsStringAsync());

        using var otherClient = _factory.CreateAuthenticatedClient(otherKey);
        var otherHtml = await otherClient.GetStringAsync(RoutePaths.AccountRecurring);
        Assert.Contains(otherSchedule, otherHtml);
        Assert.DoesNotContain(ownerSchedule, otherHtml);

        using var freeClient = _factory.CreateAuthenticatedClient(freeKey);
        var freeHtml = await freeClient.GetStringAsync(RoutePaths.AccountRecurring);
        Assert.Contains("id=\"recurring-body\"", freeHtml);
        Assert.DoesNotContain(ownerSchedule, freeHtml);
        Assert.DoesNotContain("data-ai-action=\"recurring.create\"", freeHtml);
    }

    [Theory]
    [InlineData(RoutePaths.AccountUsage, "usage-card")]
    [InlineData(RoutePaths.AccountOnboarding, "onboarding-card")]
    [InlineData(RoutePaths.AccountReferral, "referral-card")]
    [InlineData(RoutePaths.AccountDevices, "devices-card")]
    [InlineData(RoutePaths.AccountDownloads, "downloads-card")]
    [InlineData(RoutePaths.AccountProfile, "profile-card")]
    public async Task AccountCard_DirectGet_HasCardAndLayout(string route, string cardId)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"id=\"{cardId}\"", html);
        Assert.Contains("</html>", html, StringComparison.OrdinalIgnoreCase);
    }
}
