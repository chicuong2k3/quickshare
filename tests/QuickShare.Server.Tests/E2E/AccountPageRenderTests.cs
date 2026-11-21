using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using QuickShare.Data;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// /account composes eight child components that each read the database, and static SSR starts a
/// child's OnInitializedAsync before the previous one has finished. When those children shared one
/// scoped AppDbContext, every load of the page threw "A second operation was started on this
/// context instance" and the request ended in a 500 — the bug this test pins down. It asserts the
/// whole page renders with its children intact rather than merely that the status code is 200,
/// because a partial render is exactly how the failure looked to a signed-in user.
/// </summary>
public sealed class AccountPageRenderTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AccountPageRenderTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Account_page_renders_every_child_section()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/account");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("A second operation was started", html, StringComparison.Ordinal);
        Assert.Contains("id=\"profile-card\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_security_tab_renders_sessions_activity_and_devices()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/account/security");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("A second operation was started", html, StringComparison.Ordinal);
        Assert.Contains("id=\"security-activity-card\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"devices-card\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_devices_page_renders() // devices still directly routable at /account/devices
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/account/devices");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("A second operation was started", html, StringComparison.Ordinal);
        Assert.Contains("id=\"devices-card\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoutePaths.Account)]
    [InlineData(RoutePaths.AccountDeliveries)]
    [InlineData(RoutePaths.AccountDeveloper)]
    [InlineData(RoutePaths.AccountReferral)]
    [InlineData(RoutePaths.AccountSecurity)]
    [InlineData(RoutePaths.AccountFileRequests)]
    [InlineData(RoutePaths.AccountWorkspace)]
    [InlineData(RoutePaths.AccountBilling)]
    [InlineData(RoutePaths.AccountInvoices)]
    [InlineData(RoutePaths.AccountWorkspaceFolders)]
    [InlineData(RoutePaths.AccountWorkspaceSeats)]
    [InlineData(RoutePaths.AccountWorkspaceChannel)]
    [InlineData(RoutePaths.AccountWorkspaceAudit)]
    [InlineData(RoutePaths.AccountWorkspaceBrand)]
    [InlineData(RoutePaths.AccountWorkspaceDomain)]
    [InlineData(RoutePaths.AccountWorkspaceReport)]
    public async Task Account_tab_routes_render_unified_shell_and_title(string route)
    {
        const string accountShellClass = "account-shell";
        const string accountTabsClass = "account-tabs";
        const string accountShellHeadClass = "account-shell__head";
        const string accountShellTitleClass = "account-shell__title";

        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(accountShellClass, html, StringComparison.Ordinal);
        Assert.Contains(accountTabsClass, html, StringComparison.Ordinal);
        Assert.Contains(accountShellHeadClass, html, StringComparison.Ordinal);
        Assert.Contains(accountShellTitleClass, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The render test above cannot actually reproduce the race: the in-memory provider completes
    /// queries synchronously, so siblings never overlap and the page renders either way. This guard
    /// is the test that fails when the bug returns — no component may take the request-scoped
    /// context, because a component that does inherits a context its siblings are still using.
    /// </summary>
    [Fact]
    public void No_component_injects_the_scoped_db_context()
    {
        var offenders = typeof(QuickShare.App).Assembly.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(property => property.PropertyType == typeof(AppDbContext))
                .Where(property => property.GetCustomAttribute<InjectAttribute>() is not null)
                .Select(property => type.FullName + "." + property.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }
}
