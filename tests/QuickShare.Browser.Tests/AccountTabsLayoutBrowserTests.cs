using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Guards against layout shift and inconsistent headers across every tab in the account shell.
/// </summary>
public sealed class AccountTabsLayoutBrowserTests : IClassFixture<BrowserFixture>
{
    private const int DesktopWidth = 1440;
    private const int DesktopHeight = 900;
    private const int MobileWidth = 375;
    private const int MobileHeight = 812;
    private const double GeometryTolerancePx = 1.0;

    private const string ShellHeadTitleSelector = ".account-shell__head h1";
    private const string AccountTabsSelector = ".account-tabs";
    private const string ActiveTabSelector = ".account-tabs__item--active";
    private const string ShellContentSelector = ".account-shell__content";
    private const string AriaCurrentAttr = "aria-current";
    private const string AriaCurrentPageValue = "page";
    private const string RowFlexDirection = "row";

    private static readonly string[] AccountTabRoutes =
    [
        RoutePaths.Account,
        RoutePaths.AccountDeliveries,
        RoutePaths.AccountDeveloper,
        RoutePaths.AccountReferral,
        RoutePaths.AccountSecurity,
        RoutePaths.AccountFileRequests,
        RoutePaths.AccountWorkspace,
        RoutePaths.AccountBilling,
    ];

    private static readonly string[] MobileSampleRoutes =
    [
        RoutePaths.Account,
        RoutePaths.AccountDeliveries,
        RoutePaths.AccountReferral,
        RoutePaths.AccountWorkspace,
        RoutePaths.AccountBilling,
    ];

    private readonly BrowserFixture _fixture;

    public AccountTabsLayoutBrowserTests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DesktopTabs_KeepFixedGeometryAndConsistentHeaderAcrossAllTabs()
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);

        var tabBoxes = new List<(string Route, LocatorBoundingBoxResult Box)>();
        var contentBoxes = new List<(string Route, LocatorBoundingBoxResult Box)>();

        foreach (var route in AccountTabRoutes)
        {
            await page.GotoAsync(_fixture.BaseUrl + route);

            var title = page.Locator(ShellHeadTitleSelector);
            await Assertions.Expect(title).ToBeVisibleAsync();
            var titleText = (await title.TextContentAsync())?.Trim();
            Assert.False(string.IsNullOrWhiteSpace(titleText), $"{route} rendered an empty title");

            var activeTab = page.Locator(ActiveTabSelector);
            await Assertions.Expect(activeTab).ToHaveCountAsync(1);
            await Assertions.Expect(activeTab).ToHaveAttributeAsync(AriaCurrentAttr, AriaCurrentPageValue);

            var tabsBox = await page.Locator(AccountTabsSelector).BoundingBoxAsync();
            Assert.NotNull(tabsBox);
            tabBoxes.Add((route, tabsBox));

            var contentBox = await page.Locator(ShellContentSelector).BoundingBoxAsync();
            Assert.NotNull(contentBox);
            contentBoxes.Add((route, contentBox));
        }

        var baselineTabs = tabBoxes[0].Box;
        foreach (var (route, box) in tabBoxes)
        {
            Assert.True(
                Math.Abs(box.X - baselineTabs.X) <= GeometryTolerancePx,
                $"{route} tabs X shifted from {baselineTabs.X} to {box.X}");
            Assert.True(
                Math.Abs(box.Y - baselineTabs.Y) <= GeometryTolerancePx,
                $"{route} tabs Y shifted from {baselineTabs.Y} to {box.Y}");
            Assert.True(
                Math.Abs(box.Width - baselineTabs.Width) <= GeometryTolerancePx,
                $"{route} tabs width shifted from {baselineTabs.Width} to {box.Width}");
        }

        var baselineContent = contentBoxes[0].Box;
        foreach (var (route, box) in contentBoxes)
        {
            Assert.True(
                Math.Abs(box.X - baselineContent.X) <= GeometryTolerancePx,
                $"{route} content X shifted from {baselineContent.X} to {box.X}");
            Assert.True(
                Math.Abs(box.Width - baselineContent.Width) <= GeometryTolerancePx,
                $"{route} content width shifted from {baselineContent.Width} to {box.Width}");
        }
    }

    [Fact]
    public async Task MobileTabs_RenderHorizontalRowWithoutShiftOrOverflow()
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(MobileWidth, MobileHeight);

        var tabBoxes = new List<(string Route, LocatorBoundingBoxResult Box)>();

        foreach (var route in MobileSampleRoutes)
        {
            await page.GotoAsync(_fixture.BaseUrl + route);

            var tabs = page.Locator(AccountTabsSelector);
            await Assertions.Expect(tabs).ToBeVisibleAsync();

            var flexDirection = await tabs.EvaluateAsync<string>(
                "el => getComputedStyle(el).flexDirection");
            Assert.Equal(RowFlexDirection, flexDirection);

            var tabsBox = await tabs.BoundingBoxAsync();
            Assert.NotNull(tabsBox);
            tabBoxes.Add((route, tabsBox));

            var hasNoOverflow = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1");
            Assert.True(hasNoOverflow, $"{route} overflowed horizontally on mobile");
        }

        var baseline = tabBoxes[0].Box;
        foreach (var (route, box) in tabBoxes)
        {
            Assert.True(
                Math.Abs(box.X - baseline.X) <= GeometryTolerancePx,
                $"{route} mobile tabs X shifted from {baseline.X} to {box.X}");
            Assert.True(
                Math.Abs(box.Y - baseline.Y) <= GeometryTolerancePx,
                $"{route} mobile tabs Y shifted from {baseline.Y} to {box.Y}");
            Assert.True(
                Math.Abs(box.Width - baseline.Width) <= GeometryTolerancePx,
                $"{route} mobile tabs width shifted from {baseline.Width} to {box.Width}");
        }
    }
}
