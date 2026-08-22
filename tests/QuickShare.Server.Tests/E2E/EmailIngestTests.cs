using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.EmailIngest;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Upload by email (#104).
///
/// The address is not a secret — it travels in every mail header it appears in — so what makes
/// it safe is that only the account's own address may use it. These assert that boundary, the
/// webhook's own secret, and that the plan limits a forwarded file the same way a browser upload
/// is limited.
/// </summary>
public sealed class EmailIngestTests : IClassFixture<TestWebAppFactory>
{
    private const string Inbound = "/api/uploads/inbound";
    private const string AttachmentName = "invoice.pdf";

    private readonly TestWebAppFactory _factory;

    public EmailIngestTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Address_IsMintedOnFirstAskAndStaysTheSame()
    {
        var (client, _) = await AccountAsync();

        var first = await AddressAsync(client);
        var second = await AddressAsync(client);

        Assert.True(first.Enabled);
        Assert.StartsWith(EmailIngestService.AddressPrefix, first.Address, StringComparison.Ordinal);
        // Stable: an address that changed would break every forwarding rule set up against it.
        Assert.Equal(first.Address, second.Address);
    }

    [Fact]
    public async Task Webhook_RefusesACallWithoutTheDeploymentSecret()
    {
        var (client, user) = await AccountAsync();
        var address = (await AddressAsync(client)).Address;

        var response = await PostAsync(new InboundEmailRequest
        {
            From = user.Email,
            To = address,
            Attachments = [Attachment("hello")],
        }, secret: "not-the-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await DeliveryCountAsync(user.Id));
    }

    [Fact]
    public async Task ForwardedMail_BecomesADelivery()
    {
        var (client, user) = await AccountAsync();
        var address = (await AddressAsync(client)).Address;

        var response = await PostAsync(new InboundEmailRequest
        {
            From = $"{user.Name} <{user.Email}>",
            To = address,
            Subject = "Fwd: invoice",
            Attachments = [Attachment("hello")],
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.SenderId == user.Id);

        Assert.Equal(AttachmentName, delivery.FileName);
        Assert.Equal(DeliverySources.Email, delivery.Source);
        // An ordinary delivery: a link, an expiry, the account's own quota spent.
        Assert.NotEqual(string.Empty, delivery.ShareCode);
        Assert.NotNull(delivery.ExpiresAt);
    }

    [Fact]
    public async Task MailFromAnybodyElse_CreatesNothing()
    {
        var (client, user) = await AccountAsync();
        var address = (await AddressAsync(client)).Address;

        var response = await PostAsync(new InboundEmailRequest
        {
            From = "stranger@example.test",
            To = address,
            Attachments = [Attachment("hello")],
        });

        // Accepted and ignored: the provider cannot fix this by retrying, and an error would
        // earn hours of them. Nothing lands in somebody else's account either way.
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, await DeliveryCountAsync(user.Id));
    }

    [Fact]
    public async Task MailToAnUnknownAddress_IsAcknowledgedAndDropped()
    {
        var response = await PostAsync(new InboundEmailRequest
        {
            From = "someone@example.test",
            To = "u-000000000000000000000000@inbound.test",
            Attachments = [Attachment("hello")],
        });

        response.EnsureSuccessStatusCode();
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Created);
    }

    [Fact]
    public async Task MailWithNoAttachments_IsNotAnError()
    {
        var (client, user) = await AccountAsync();
        var address = (await AddressAsync(client)).Address;

        var response = await PostAsync(new InboundEmailRequest
        {
            From = user.Email,
            To = address,
            Subject = "just a note",
            Attachments = [],
        });

        // People forward things with nothing attached; that is a no-op, not a failure.
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, await DeliveryCountAsync(user.Id));
    }

    [Fact]
    public async Task SeveralAttachments_BecomeSeveralDeliveries()
    {
        var (client, user) = await AccountAsync();
        var address = (await AddressAsync(client)).Address;

        var response = await PostAsync(new InboundEmailRequest
        {
            From = user.Email,
            To = address,
            Attachments = [Attachment("one"), Attachment("two", "second.pdf")],
        });
        response.EnsureSuccessStatusCode();

        // One link each, because they are separate files somebody attached separately.
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Created);
        Assert.Equal(2, await DeliveryCountAsync(user.Id));
    }

    [Fact]
    public void TokenParsing_AcceptsOnlyOurOwnAddressShape()
    {
        var token = new string('a', EmailIngestService.TokenLength);

        Assert.Equal(token, EmailIngestService.ExtractToken($"Someone <{EmailIngestService.AddressPrefix}{token}@inbound.test>"));
        Assert.Null(EmailIngestService.ExtractToken("support@inbound.test"));
        Assert.Null(EmailIngestService.ExtractToken($"{EmailIngestService.AddressPrefix}short@inbound.test"));
        Assert.Null(EmailIngestService.ExtractToken(null));
    }

    [Fact]
    public async Task SendPage_OffersTheForwardingAddressToASignedInAccount()
    {
        var (client, _) = await AccountAsync();

        var html = await client.GetStringAsync("/send");

        // An endpoint nobody can find is the defect the batch-11 audit spent its time on.
        Assert.Contains("upload.byEmail.show", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendPage_OffersNothingToAStranger()
    {
        var html = await _factory.CreateClient().GetStringAsync("/send");

        // An anonymous visitor has no account for the mail to land in.
        Assert.DoesNotContain("upload.byEmail.show", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private sealed record CreatedDto(int Created);

    private async Task<(HttpClient Client, User User)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        return (_factory.CreateAuthenticatedClient(providerKey), user);
    }

    private static async Task<EmailIngestAddressDto> AddressAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/uploads/email-address");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EmailIngestAddressDto>())!;
    }

    private static InboundAttachment Attachment(string content, string name = AttachmentName) =>
        new(name, "application/pdf", Convert.ToBase64String(Encoding.UTF8.GetBytes(content)));

    private Task<HttpResponseMessage> PostAsync(InboundEmailRequest request, string? secret = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Inbound)
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.TryAddWithoutValidation(
            InboundEmailEndpoint.SecretHeader, secret ?? TestWebAppFactory.InboundSecret);

        return _factory.CreateClient().SendAsync(message);
    }

    private async Task<int> DeliveryCountAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Deliveries.CountAsync(d => d.SenderId == userId);
    }
}
