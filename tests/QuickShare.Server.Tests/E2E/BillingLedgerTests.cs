using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments;
using QuickShare.Features.Payments.Billing;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Seats;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The money side of a workspace: per-seat billing (#278), Studio overage (#141), VAT-ready
/// invoices (#112) and purchase orders with net terms (#192).
///
/// The invariants worth defending: a seat past the included pool is charged once, prorated, and
/// only on growth; Studio keeps uploading past its quota while everyone else still hard-stops;
/// an issued invoice freezes the buyer's details and takes a unique number; and asking for net
/// terms is not the same as being granted them.
/// </summary>
public sealed class BillingLedgerTests : IClassFixture<TestWebAppFactory>
{
    private const string ValidTaxCode = "0101243150";
    private const string CompanyName = "Cong ty TNHH Demo";

    private readonly TestWebAppFactory _factory;

    public BillingLedgerTests(TestWebAppFactory factory) => _factory = factory;

    // ── Seat arithmetic (#278), pinned without a workspace around it ──

    [Fact]
    public void Seats_BillOnlyPastTheIncludedPool()
    {
        Assert.Equal(0, SeatBilling.BillableSeats(PlanTierLimits.Pro, 3));
        Assert.Equal(2, SeatBilling.BillableSeats(PlanTierLimits.Pro, 5));
        // Studio includes more seats, so the same headcount costs nothing there.
        Assert.Equal(0, SeatBilling.BillableSeats(PlanTierLimits.Studio, 5));
    }

    [Fact]
    public void Seats_ProrateToTheRestOfThePeriodAndNeverToZero()
    {
        var periodEnd = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthly = 30_000m;

        // Half a 31-day period left, one seat: about half price.
        var midMonth = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var half = SeatBilling.Prorate(monthly, 1, midMonth, periodEnd);
        Assert.InRange(half, 14_000m, 16_000m);

        // The last hours of a period still cost something — rounding them away would make the
        // last day of every month free.
        var lastDay = new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc);
        Assert.True(SeatBilling.Prorate(monthly, 1, lastDay, periodEnd) > 0m);

        Assert.Equal(0m, SeatBilling.Prorate(monthly, 0, midMonth, periodEnd));
    }

    [Fact]
    public async Task Seats_GrowingPastTheIncludedPoolChargesTheDraftOnce()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client, seats: 3);

        // Pro includes 3, so the first three seats are free.
        Assert.Empty(await LinesAsync(ownerId, InvoiceLineKind.Seat));

        var grow = await client.PatchAsJsonAsync("/api/workspace", new UpdateWorkspaceRequest { Seats = 5 });
        Assert.Equal(HttpStatusCode.OK, grow.StatusCode);

        var lines = await LinesAsync(ownerId, InvoiceLineKind.Seat);
        var line = Assert.Single(lines);
        Assert.Equal(2m, line.Quantity);
        Assert.True(line.Amount > 0);

        // Shrinking frees seats for next period; it does not produce a credit note, because
        // this codebase has no refund flow and faking one would be worse than not having it.
        var shrink = await client.PatchAsJsonAsync("/api/workspace", new UpdateWorkspaceRequest { Seats = 3 });
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);
        Assert.Single(await LinesAsync(ownerId, InvoiceLineKind.Seat));
    }

    [Fact]
    public async Task Seats_ReportCountsPendingInvitesAsClaimed()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client, seats: 3);
        await client.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "pending@example.com", Role = "member" });

        var report = await client.GetFromJsonAsync<SeatReportDto>("/api/workspace/seats");

        Assert.NotNull(report);
        // Owner + outstanding invite. Billing on acceptance would let a workspace park invites
        // on seats it never pays for.
        Assert.Equal(2, report.ClaimedSeats);
        Assert.Equal(3, report.IncludedSeats);
        Assert.Equal(0, report.BillableSeats);
        Assert.True(report.ProratedNextSeatAmount > 0);
    }

    [Fact]
    public async Task Seats_RosterExportIsAdminOnly()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(ownerClient, seats: 3);

        var export = await ownerClient.GetAsync("/api/workspace/seats/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.StartsWith("email,name,role,status,joined_utc,seat_unit_amount,currency", csv, StringComparison.Ordinal);

        var (strangerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var refused = await strangerClient.GetAsync("/api/workspace/seats/export");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ── Overage (#141) ──

    [Fact]
    public async Task Overage_StudioKeepsGoingPastTheQuotaAndIsMetered()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Studio);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quota = PlanTierLimits.GetMonthlyQuota(PlanTierLimits.Studio)!.Value;

            // Fill the pool, then push a gigabyte past it.
            var service = new QuotaService(db);
            await service.RecordAsync(ownerId, PlanTierLimits.Studio, quota, CancellationToken.None);
            await db.SaveChangesAsync();

            const long OneGb = 1024L * 1024 * 1024;
            await service.RecordAsync(ownerId, PlanTierLimits.Studio, OneGb, CancellationToken.None);
            await db.SaveChangesAsync();

            // The next upload is allowed rather than refused: a hard stop mid-delivery is the
            // wrong failure for a team on a deadline.
            var check = await new QuotaService(db).CheckAsync(
                ownerId, PlanTierLimits.Studio, OneGb, CancellationToken.None);
            Assert.False(check.IsExceeded);
            Assert.True(check.IsMetered);
        }

        var line = Assert.Single(await LinesAsync(ownerId, InvoiceLineKind.Overage));
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(PlanCatalog.OverageGbAmount(PlanTierLimits.Studio), line.UnitAmount);
    }

    [Fact]
    public async Task Overage_IsOneRunningLineRatherThanOnePerUpload()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Studio);
        const long OneGb = 1024L * 1024 * 1024;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quota = PlanTierLimits.GetMonthlyQuota(PlanTierLimits.Studio)!.Value;
            var service = new QuotaService(db);

            await service.RecordAsync(ownerId, PlanTierLimits.Studio, quota, CancellationToken.None);
            await db.SaveChangesAsync();

            for (var i = 0; i < 3; i++)
            {
                await new QuotaService(db).RecordAsync(ownerId, PlanTierLimits.Studio, OneGb, CancellationToken.None);
                await db.SaveChangesAsync();
            }
        }

        // Three uploads past the pool, one charge — an afternoon of work should not produce a
        // wall of line items.
        var line = Assert.Single(await LinesAsync(ownerId, InvoiceLineKind.Overage));
        Assert.Equal(3m, line.Quantity);
    }

    [Fact]
    public async Task Overage_ProStillHardBlocksAtTheQuota()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quota = PlanTierLimits.GetMonthlyQuota(PlanTierLimits.Pro)!.Value;

        await new QuotaService(db).RecordAsync(ownerId, PlanTierLimits.Pro, quota, CancellationToken.None);
        await db.SaveChangesAsync();

        var check = await new QuotaService(db).CheckAsync(ownerId, PlanTierLimits.Pro, 1024, CancellationToken.None);

        // An unbounded bill is a worse surprise than a refused upload when nobody agreed to
        // pay for overflow.
        Assert.True(check.IsExceeded);
        Assert.False(check.IsMetered);
        Assert.Empty(await LinesAsync(ownerId, InvoiceLineKind.Overage));
    }

    [Fact]
    public async Task Overage_ThresholdAlertFiresOncePerLevel()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var quota = PlanTierLimits.GetMonthlyQuota(PlanTierLimits.Pro)!.Value;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Two writes that both sit above 80% but below 100%.
        await new QuotaService(db).RecordAsync(ownerId, PlanTierLimits.Pro, (long)(quota * 0.85), CancellationToken.None);
        await db.SaveChangesAsync();
        await new QuotaService(db).RecordAsync(ownerId, PlanTierLimits.Pro, 1024, CancellationToken.None);
        await db.SaveChangesAsync();

        var alerts = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == ownerId && n.Type == NotificationType.QuotaThreshold)
            .ToListAsync();

        // One alert, not one per upload — an alert that repeats is an alert people mute.
        var alert = Assert.Single(alerts);
        Assert.Equal("80%", alert.Title);
    }

    // ── Invoicing (#112) ──

    [Fact]
    public async Task Invoice_IssuingFreezesTheBuyerDetailsAndTakesANumber()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        var save = await client.PutAsJsonAsync("/api/billing/profile", new BillingProfileRequest
        {
            CompanyName = CompanyName,
            TaxCode = ValidTaxCode,
            Address = "1 Nguyen Hue, HCMC",
            PoNumber = "PO-4417",
            NetTermsDays = 0,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        Invoice issued;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new InvoiceService(db);
            await service.AddOrUpdateLineAsync(ownerId, InvoiceLineKind.Plan, "Pro (monthly)", 1m, 50_000m, CancellationToken.None);
            issued = (await service.IssueAsync(ownerId, paymentEventId: null, alreadyPaid: true, CancellationToken.None))!;
            await db.SaveChangesAsync();
        }

        Assert.StartsWith($"QS-{DateTime.UtcNow.Year}-", issued.Number, StringComparison.Ordinal);
        Assert.Equal(InvoiceStatus.Paid, issued.Status);
        // Frozen copies: the invoice has to keep saying what it said even after the buyer moves.
        Assert.Equal(CompanyName, issued.CompanyName);
        Assert.Equal(ValidTaxCode, issued.TaxCode);
        Assert.Equal("PO-4417", issued.PoNumber);
        // 50,000 + 10% VAT.
        Assert.Equal(55_000m, issued.Total);
    }

    [Fact]
    public async Task Invoice_AnEmptyPeriodIsNotBilled()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var issued = await new InvoiceService(db).IssueAsync(
            ownerId, paymentEventId: null, alreadyPaid: true, CancellationToken.None);

        // Zero-total paperwork is how a buyer's accounting team learns to ignore us.
        Assert.Null(issued);
    }

    [Fact]
    public async Task Invoice_ListIncludesTheOpenDraftAndExportDoesNot()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new InvoiceService(db).AddOrUpdateLineAsync(
                ownerId, InvoiceLineKind.Seat, "1 seat", 1m, 25_000m, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<List<InvoiceDto>>("/api/billing/invoices");
        Assert.NotNull(list);
        // Visible mid-month: recording charges when they happen is pointless if the buyer
        // cannot see them until issue day.
        Assert.Contains(list, i => i.Status == InvoiceStatus.Draft);

        var export = await client.GetAsync("/api/billing/invoices/export");
        var csv = await export.Content.ReadAsStringAsync();
        Assert.DoesNotContain(InvoiceStatus.Draft, csv, StringComparison.Ordinal);
    }

    // ── PO and net terms (#192) ──

    [Fact]
    public async Task NetTerms_AreInertUntilAnAdminApprovesThem()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await client.PutAsJsonAsync("/api/billing/profile", new BillingProfileRequest
        {
            CompanyName = CompanyName,
            TaxCode = ValidTaxCode,
            NetTermsDays = 30,
        });

        var requested = await client.GetFromJsonAsync<BillingProfileDto>("/api/billing/profile");
        Assert.NotNull(requested);
        Assert.Equal(30, requested.NetTermsDays);
        // Asking is not being granted: self-service credit is how a SaaS discovers bad debt.
        Assert.False(requested.NetTermsApproved);

        await IssueAsync(ownerId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.AsNoTracking()
                .Where(i => i.UserId == ownerId && i.Status != InvoiceStatus.Draft)
                .SingleAsync();
            Assert.Equal(0, invoice.NetTermsDays);
            Assert.Equal(invoice.IssuedAt, invoice.DueAt);
        }

        // The default test client is the configured admin, so it can approve.
        var approve = await _factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/admin/billing/net-terms", new ApproveNetTermsRequest { UserId = ownerId, Approved = true });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var approved = await client.GetFromJsonAsync<BillingProfileDto>("/api/billing/profile");
        Assert.NotNull(approved);
        Assert.True(approved.NetTermsApproved);
    }

    [Fact]
    public async Task NetTerms_ChangingTheRequestClearsAPriorApproval()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await client.PutAsJsonAsync("/api/billing/profile",
            new BillingProfileRequest { CompanyName = CompanyName, TaxCode = ValidTaxCode, NetTermsDays = 30 });
        await _factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/admin/billing/net-terms", new ApproveNetTermsRequest { UserId = ownerId, Approved = true });

        await client.PutAsJsonAsync("/api/billing/profile",
            new BillingProfileRequest { CompanyName = CompanyName, TaxCode = ValidTaxCode, NetTermsDays = 60 });

        var profile = await client.GetFromJsonAsync<BillingProfileDto>("/api/billing/profile");
        Assert.NotNull(profile);
        Assert.Equal(60, profile.NetTermsDays);
        // "Approved for 30 days" is not approval for 60.
        Assert.False(profile.NetTermsApproved);
    }

    [Fact]
    public async Task NetTerms_ApprovedInvoiceGetsADueDateAndGoesOverdue()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        await client.PutAsJsonAsync("/api/billing/profile",
            new BillingProfileRequest { CompanyName = CompanyName, TaxCode = ValidTaxCode, NetTermsDays = 30 });
        await _factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/admin/billing/net-terms", new ApproveNetTermsRequest { UserId = ownerId, Approved = true });

        Guid invoiceId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new InvoiceService(db);
            await service.AddOrUpdateLineAsync(ownerId, InvoiceLineKind.Plan, "Pro (monthly)", 1m, 50_000m, CancellationToken.None);
            var invoice = (await service.IssueAsync(ownerId, null, alreadyPaid: false, CancellationToken.None))!;
            invoiceId = invoice.Id;
            Assert.Equal(InvoiceStatus.Issued, invoice.Status);
            Assert.Equal(30, invoice.NetTermsDays);

            // Backdate it past the due date and let the sweep find it.
            invoice.DueAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        await RunOverdueSweepAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceStatus.Overdue, invoice.Status);

            Assert.Contains(await db.Notifications.AsNoTracking().ToListAsync(),
                n => n.UserId == ownerId && n.Type == NotificationType.InvoiceOverdue);
        }

        // A second pass is a no-op — otherwise the buyer gets the same dunning note hourly.
        await RunOverdueSweepAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alerts = await db.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == ownerId && n.Type == NotificationType.InvoiceOverdue);
            Assert.Equal(1, alerts);
        }
    }

    [Fact]
    public async Task Profile_RefusesAMalformedTaxCode()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);

        var response = await client.PutAsJsonAsync("/api/billing/profile",
            new BillingProfileRequest { CompanyName = CompanyName, TaxCode = "not-a-tax-code" });

        // It is copied onto a legal document the buyer files with their tax authority.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Profile_RefusesTermsOutsideTheAllowedSet()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);

        var response = await client.PutAsJsonAsync("/api/billing/profile",
            new BillingProfileRequest { NetTermsDays = 45 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_AreRefusedToOrdinaryUsers()
    {
        var (_, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var outsider = await NonAdminClientAsync();

        var terms = await outsider.PostAsJsonAsync("/api/admin/billing/net-terms",
            new ApproveNetTermsRequest { UserId = ownerId, Approved = true });

        // Extending credit is a risk decision about a specific customer, not a switch the
        // customer gets to flip for themselves.
        Assert.Equal(HttpStatusCode.Forbidden, terms.StatusCode);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> ClientAsync(string plan)
    {
        // One account per case: usage, seats and invoices are all account-wide state.
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, plan);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    /// <summary>
    /// A signed-in client that is deliberately not the configured platform admin — the default
    /// helper hands out the admin email, which would make every admin-gate assertion vacuous.
    /// </summary>
    private async Task<HttpClient> NonAdminClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, providerKey);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, "outsider@example.com");
        return client;
    }

    private static async Task<WorkspaceDto> CreateWorkspaceAsync(HttpClient client, int seats)
    {
        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = "Billing test", Seats = seats });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private async Task<List<InvoiceLine>> LinesAsync(Guid userId, string kind)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.InvoiceLines
            .AsNoTracking()
            .Where(l => l.Kind == kind && db.Invoices.Any(i => i.Id == l.InvoiceId && i.UserId == userId))
            .ToListAsync();
    }

    private async Task IssueAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new InvoiceService(db);
        await service.AddOrUpdateLineAsync(userId, InvoiceLineKind.Plan, "Pro (monthly)", 1m, 50_000m, CancellationToken.None);
        await service.IssueAsync(userId, paymentEventId: null, alreadyPaid: false, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private async Task RunOverdueSweepAsync()
    {
        var job = new InvoiceOverdueJob(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<QuickShare.Infrastructure.Email.EmailSender>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InvoiceOverdueJob>.Instance);

        await job.SweepAsync();
    }
}
