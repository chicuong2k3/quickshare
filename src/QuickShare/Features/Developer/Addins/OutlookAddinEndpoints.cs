using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Developer.Addins;

/// <summary>
/// The Outlook add-in's manifest (issue #194).
///
/// Served rather than shipped as a file because every URL in it is absolute and must match the
/// deployment: a manifest with somebody else's hostname in it installs and then silently does
/// nothing. Generated from the same <c>Email:BaseUrl</c> every other absolute link uses, so
/// there is one place that knows where this deployment lives.
///
/// The id is stable and derived from nothing — Outlook keys the installed add-in on it, so it
/// must not change between deploys or every org's install becomes a second, duplicate entry.
/// </summary>
public static class OutlookAddinEndpoints
{
    /// <summary>Fixed for the lifetime of the add-in. Changing it re-installs, it does not update.</summary>
    public const string AddinId = "1f4f3e6a-1f2b-4a3d-9c8e-6d5b7a1c2e30";
    public const string ManifestPath = "/addins/outlook/manifest.xml";
    public const string TaskPanePath = "/addins/outlook";
    private const string ManifestContentType = "application/xml";

    public static void MapOutlookAddinEndpoints(this WebApplication app)
    {
        app.MapGet(ManifestPath, (IOptions<EmailOptions> emailOptions) =>
        {
            var origin = emailOptions.Value.BaseUrl.TrimEnd('/');

            return Results.Content(Build(origin), ManifestContentType);
        }).AllowAnonymous();
    }

    /// <summary>
    /// Office Add-in manifest, MailApp, task pane in compose.
    ///
    /// Only the compose surface is declared. "Share this attachment as a link" is a thing you do
    /// while writing a mail; the read surface would be a different feature (saving somebody
    /// else's attachment), and declaring a button that does the wrong thing is worse than not
    /// having one.
    /// </summary>
    public static string Build(string origin) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                   xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
                   xmlns:mailappor="http://schemas.microsoft.com/office/mailappversionoverrides/1.0"
                   xsi:type="MailApp">
          <Id>{AddinId}</Id>
          <Version>1.0.0.0</Version>
          <ProviderName>QuickShareCloud</ProviderName>
          <DefaultLocale>en-US</DefaultLocale>
          <DisplayName DefaultValue="QuickShare" />
          <Description DefaultValue="Replace a large attachment with a QuickShare link." />
          <IconUrl DefaultValue="{origin}/icon-64.png" />
          <HighResolutionIconUrl DefaultValue="{origin}/icon-128.png" />
          <SupportUrl DefaultValue="{origin}/help" />
          <AppDomains>
            <AppDomain>{origin}</AppDomain>
          </AppDomains>
          <Hosts>
            <Host Name="Mailbox" />
          </Hosts>
          <Requirements>
            <Sets>
              <Set Name="Mailbox" MinVersion="1.8" />
            </Sets>
          </Requirements>
          <FormSettings>
            <Form xsi:type="ItemEdit">
              <DesktopSettings>
                <SourceLocation DefaultValue="{origin}{TaskPanePath}" />
              </DesktopSettings>
            </Form>
          </FormSettings>
          <Permissions>ReadWriteItem</Permissions>
          <Rule xsi:type="RuleCollection" Mode="Or">
            <Rule xsi:type="ItemIs" ItemType="Message" FormType="Edit" />
          </Rule>
        </OfficeApp>
        """;
}
