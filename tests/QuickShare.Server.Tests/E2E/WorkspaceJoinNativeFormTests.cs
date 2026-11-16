using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class WorkspaceJoinNativeFormTests : IClassFixture<TestWebAppFactory>
{
    private const string TokenField = "joinToken";
    private const string KindField = "joinKind";
    private readonly TestWebAppFactory _factory;

    public WorkspaceJoinNativeFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task InviteForm_RequiresAntiforgeryAndAcceptsSeat()
    {
        var owner = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString(), PlanTierLimits.Pro);
        var memberKey = Guid.CreateVersion7().ToString();
        var member = await _factory.SeedUserAsync(memberKey);
        var token = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Id = workspaceId, OwnerId = owner.Id, Name = "Invite workspace" });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                InviteEmail = member.Email,
                InviteTokenHash = WorkspaceService.HashToken(token),
                InviteExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        using var client = Client(memberKey);
        var html = await client.GetStringAsync($"{RoutePaths.WorkspaceJoin}?token={token}");
        using (var missingToken = Form(token, "invite", null))
        {
            using var rejected = await client.PostAsync(RoutePaths.WorkspaceJoin, missingToken);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using (var form = Form(token, "invite", AntiforgeryHelper.ExtractToken(html)))
        {
            using var accepted = await client.PostAsync(RoutePaths.WorkspaceJoin, form);
            Assert.True(accepted.StatusCode == HttpStatusCode.Redirect,
                $"Expected redirect, got {accepted.StatusCode}: {await accepted.Content.ReadAsStringAsync()}");
            Assert.Equal(RoutePaths.AccountWorkspace, accepted.Headers.Location?.OriginalString);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await verifyDb.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.WorkspaceId == workspaceId);
        Assert.Equal(member.Id, seat.UserId);
        Assert.Equal(PlanTierLimits.Pro, await verifyDb.Users.Where(u => u.Id == member.Id).Select(u => u.Plan).SingleAsync());
    }

    [Fact]
    public async Task TransferForm_RejectsAnotherUserAndAcceptsNamedSuccessor()
    {
        var owner = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString(), PlanTierLimits.Pro);
        var successorKey = Guid.CreateVersion7().ToString();
        var successor = await _factory.SeedUserAsync(successorKey, PlanTierLimits.Pro);
        var otherKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(otherKey, PlanTierLimits.Pro);
        var token = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Id = workspaceId, OwnerId = owner.Id, Name = "Transfer workspace" });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = owner.Id,
                InviteEmail = owner.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = successor.Id,
                InviteEmail = successor.Email,
                JoinedAt = DateTime.UtcNow,
            });
            db.WorkspaceTransfers.Add(new WorkspaceTransfer
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                FromUserId = owner.Id,
                ToUserId = successor.Id,
                TokenHash = WorkspaceService.HashToken(token),
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        using (var other = Client(otherKey))
        {
            var html = await other.GetStringAsync($"{RoutePaths.WorkspaceJoin}?transfer={token}");
            using var form = Form(token, "transfer", AntiforgeryHelper.ExtractToken(html));
            using var response = await other.PostAsync(RoutePaths.WorkspaceJoin, form);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Expected error page, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync());
        }

        using (var client = Client(successorKey))
        {
            var html = await client.GetStringAsync($"{RoutePaths.WorkspaceJoin}?transfer={token}");
            using var form = Form(token, "transfer", AntiforgeryHelper.ExtractToken(html));
            using var response = await client.PostAsync(RoutePaths.WorkspaceJoin, form);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(successor.Id, await verifyDb.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.OwnerId).SingleAsync());
    }

    private HttpClient Client(string key)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");
        return client;
    }

    private static FormUrlEncodedContent Form(string token, string kind, string? antiforgery)
    {
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "workspace-join",
            [TokenField] = token,
            [KindField] = kind,
        };
        if (antiforgery is not null)
        {
            fields["__RequestVerificationToken"] = antiforgery;
        }
        return new FormUrlEncodedContent(fields);
    }
}
