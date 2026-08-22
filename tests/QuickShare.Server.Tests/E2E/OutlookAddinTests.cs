using System.Net;
using System.Xml.Linq;
using QuickShare.Features.Developer.Addins;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The Outlook add-in's server side (issue #194).
///
/// What can honestly be tested here is what we serve: a manifest whose URLs point at this
/// deployment, a task pane that renders outside the app shell, and the one CSP exception the
/// pane needs — scoped to `/addins` and nowhere else. Whether Outlook then does the right thing
/// with it is a claim only a real Outlook can settle, and the batch notes say so.
/// </summary>
public sealed class OutlookAddinTests : IClassFixture<TestWebAppFactory>
{
    private static readonly XNamespace OfficeAppNs = "http://schemas.microsoft.com/office/appforoffice/1.1";

    private readonly TestWebAppFactory _factory;

    public OutlookAddinTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Manifest_IsServedAnonymouslyAsXml()
    {
        var response = await _factory.CreateClient().GetAsync(OutlookAddinEndpoints.ManifestPath);

        // An org admin fetches this before anybody has signed in to anything.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("xml", response.Content.Headers.ContentType!.MediaType!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manifest_PointsAtThisDeployment()
    {
        var body = await _factory.CreateClient().GetStringAsync(OutlookAddinEndpoints.ManifestPath);
        var manifest = XDocument.Parse(body);

        var source = manifest.Descendants(OfficeAppNs + "SourceLocation").Single().Attribute("DefaultValue")!.Value;

        // A manifest carrying somebody else's hostname installs and then silently does nothing.
        Assert.EndsWith(OutlookAddinEndpoints.TaskPanePath, source, StringComparison.Ordinal);
        Assert.StartsWith("https://", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_KeepsAStableIdAndTheComposeSurfaceOnly()
    {
        var manifest = XDocument.Parse(
            await _factory.CreateClient().GetStringAsync(OutlookAddinEndpoints.ManifestPath));

        // Outlook keys the installed add-in on the id: a new one is a duplicate install, not an
        // update.
        Assert.Equal(OutlookAddinEndpoints.AddinId, manifest.Descendants(OfficeAppNs + "Id").Single().Value);

        // Compose only. A read-surface button would be a different feature wearing this one's
        // label.
        var forms = manifest.Descendants(OfficeAppNs + "Form").ToList();
        Assert.Single(forms);
        Assert.Contains("ItemEdit", forms[0].Attributes().Select(a => a.Value));
    }

    [Fact]
    public async Task TaskPane_RendersWithoutTheAppShell()
    {
        var html = await _factory.CreateClient().GetStringAsync(OutlookAddinEndpoints.TaskPanePath);

        Assert.Contains("outlook.share", html, StringComparison.Ordinal);
        Assert.Contains("office.js", html, StringComparison.Ordinal);
        // No navbar, no footer, no cookie banner: this is a column inside somebody else's window.
        Assert.DoesNotContain("layout-footer", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskPane_GetsTheOnlyRelaxedContentSecurityPolicy()
    {
        var client = _factory.CreateClient();

        var pane = await client.GetAsync(OutlookAddinEndpoints.TaskPanePath);
        var paneCsp = pane.Headers.GetValues(HttpHeaders.ContentSecurityPolicy).Single();

        // Office serves its script and frames the pane; both are refused everywhere else.
        Assert.Contains("appsforoffice.microsoft.com", paneCsp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors https://outlook.office.com", paneCsp, StringComparison.Ordinal);

        var home = await client.GetAsync("/");
        var homeCsp = home.Headers.GetValues(HttpHeaders.ContentSecurityPolicy).Single();

        // The exception is scoped: `frame-ancestors 'none'` everywhere else is what stops the
        // rest of the app being clickjacked.
        Assert.Contains("frame-ancestors 'none'", homeCsp, StringComparison.Ordinal);
        Assert.DoesNotContain("appsforoffice.microsoft.com", homeCsp, StringComparison.Ordinal);
    }
}
