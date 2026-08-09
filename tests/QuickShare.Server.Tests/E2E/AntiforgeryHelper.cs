using System.Text.RegularExpressions;

namespace QuickShare.Server.Tests.E2E;

internal static partial class AntiforgeryHelper
{
    // Matches the hidden input Blazor renders for <AntiforgeryToken />.
    // Anchored to the input tag so unrelated occurrences of the field name
    // (e.g. near the culture switcher) cannot shadow the real token.
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""")]
    private static partial Regex TokenInput();

    public static string ExtractToken(string html)
    {
        var match = TokenInput().Match(html);
        Assert.True(match.Success, "Antiforgery token input not found in HTML");
        return match.Groups[1].Value;
    }
}
