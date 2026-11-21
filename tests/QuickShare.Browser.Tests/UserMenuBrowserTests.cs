using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Verifies that the authenticated avatar dropdown menu is positioned beneath
/// the right-side avatar trigger rather than pinned to the far-left edge.
/// </summary>
public sealed class UserMenuBrowserTests : IClassFixture<BrowserFixture>
{
    private const string AvatarTriggerSelector = ".layout-avatar-btn";
    private const string UserMenuSelector = "#layout-user-menu";
    private const string MenuItemSelector = "#layout-user-menu [data-slot='dropdown-menu-item']";
    private const string ExpectedCursor = "pointer";
    private const string TransparentBackground = "rgba(0, 0, 0, 0)";
    private const string WaitTwoFramesScript =
        "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))";

    private const int DesktopWidth = 1280;
    private const int DesktopHeight = 720;
    private const int MobileWidth = 390;
    private const int MobileHeight = 844;

    private const float AlignmentTolerancePx = 4f;
    private const float ViewportEdgeInsetPx = 8f;
    private const float MaxVerticalGapPx = 16f;
    private const float VisibilityTimeoutMs = 10_000f;

    private readonly BrowserFixture _fx;

    public UserMenuBrowserTests(BrowserFixture fx)
    {
        _fx = fx;
    }

    [Theory]
    [InlineData(DesktopWidth, DesktopHeight)]
    [InlineData(MobileWidth, MobileHeight)]
    public async Task AvatarMenu_OpensAlignedBelowRightSideTrigger(int viewportWidth, int viewportHeight)
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(viewportWidth, viewportHeight);

        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(_fx.BaseUrl + RoutePaths.Home);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var trigger = page.Locator(AvatarTriggerSelector);
        await Assertions.Expect(trigger).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        await trigger.ClickAsync();

        var menu = page.Locator(UserMenuSelector);
        await Assertions.Expect(menu).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        await page.EvaluateAsync(WaitTwoFramesScript);

        var triggerBox = await trigger.BoundingBoxAsync();
        var menuBox = await menu.BoundingBoxAsync();

        Assert.NotNull(triggerBox);
        Assert.NotNull(menuBox);
        Assert.Empty(errors);

        var menuRight = menuBox.X + menuBox.Width;
        var triggerBottom = triggerBox.Y + triggerBox.Height;
        var expectedRight = Math.Min(triggerBox.X + triggerBox.Width, viewportWidth - ViewportEdgeInsetPx);

        Assert.True(menuBox.X > 0f, $"Menu should not be pinned to left edge (menuBox.X={menuBox.X}).");
        Assert.True(menuRight <= viewportWidth, $"Menu overflows right viewport edge (menuRight={menuRight}, viewportWidth={viewportWidth}).");
        Assert.InRange(Math.Abs(menuRight - expectedRight), 0f, AlignmentTolerancePx);
        Assert.True(menuBox.Y >= triggerBottom, $"Menu should open below trigger (menuY={menuBox.Y}, triggerBottom={triggerBottom}).");
        Assert.True(menuBox.Y <= triggerBottom + MaxVerticalGapPx, $"Menu detached too far below trigger (menuY={menuBox.Y}, triggerBottom={triggerBottom}).");
    }

    [Fact]
    public async Task AvatarMenuItems_ShowPointerCursorAndHoverBackground()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);

        await page.GotoAsync(_fx.BaseUrl + RoutePaths.Home);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var trigger = page.Locator(AvatarTriggerSelector);
        await Assertions.Expect(trigger).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        await trigger.ClickAsync();

        var menu = page.Locator(UserMenuSelector);
        await Assertions.Expect(menu).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        var firstItem = page.Locator(MenuItemSelector).First;
        await Assertions.Expect(firstItem).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        var cursor = await firstItem.EvaluateAsync<string>("el => getComputedStyle(el).cursor");
        Assert.Equal(ExpectedCursor, cursor);

        var initialBackground = await firstItem.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

        await firstItem.HoverAsync();
        await page.WaitForFunctionAsync(
            "([selector, initialBg]) => { const el = document.querySelector(selector); return el && getComputedStyle(el).backgroundColor !== initialBg; }",
            new[] { MenuItemSelector, initialBackground });

        var hoveredBackground = await firstItem.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual(initialBackground, hoveredBackground);
        Assert.NotEqual(TransparentBackground, hoveredBackground);
    }
}
