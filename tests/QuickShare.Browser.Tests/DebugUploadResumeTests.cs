using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>Temporary diagnostics for the failing resume test — DELETE after use.</summary>
public sealed class DebugUploadResumeTests : IClassFixture<BrowserFixture>
{
    private const string PartUrlGlob = "http://stub.local/**";
    private const string FileInput = "input[type=file]";

    private readonly BrowserFixture _fx;

    public DebugUploadResumeTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task Debug_NetworkLoss_WhatHappens()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        var consoleLog = new List<string>();
        var pageErrors = new List<string>();
        var failedRequests = new List<string>();
        var aborted = new List<string>();

        page.Console += (_, e) => consoleLog.Add($"{e.Type}: {e.Text}");
        page.PageError += (_, e) => pageErrors.Add(e);
        page.RequestFailed += (_, e) => failedRequests.Add($"{e.Url} :: {e.Failure}");
        page.Request += (_, e) => { if (e.Url.Contains("stub.local")) aborted.Add(e.Method + " " + e.Url); };

        await page.RouteAsync(PartUrlGlob, route => route.AbortAsync());
        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.SetInputFilesAsync(FileInput, new FilePayload
        {
            Name = "resume-me.bin",
            MimeType = "application/octet-stream",
            Buffer = new byte[64 * 1024],
        });

        // Well past the 3 attempts with 1s/2s backoff.
        await Task.Delay(8000);

        var state = await page.EvaluateAsync(@"() => {
            function vis(id) { var el = document.getElementById(id); if (!el) return 'MISSING'; return el.classList.contains('hidden') ? 'hidden' : 'VISIBLE'; }
            return {
                idle: vis('upload-idle'),
                uploading: vis('upload-uploading'),
                success: vis('upload-success'),
                error: vis('upload-error'),
                resumeBtn: vis('upload-resume-btn'),
                errorMsg: document.getElementById('upload-error-msg')?.textContent,
                progressLabel: document.getElementById('upload-progress-label')?.textContent,
                filename: document.getElementById('upload-filename')?.textContent,
                shareLink: document.getElementById('share-link')?.value
            };
        }");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== STATE AFTER 8s ===");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(state));
        sb.AppendLine("=== CONSOLE ===");
        foreach (var l in consoleLog) sb.AppendLine("  " + l);
        sb.AppendLine("=== PAGE ERRORS ===");
        foreach (var l in pageErrors) sb.AppendLine("  " + l);
        sb.AppendLine("=== FAILED REQUESTS ===");
        foreach (var l in failedRequests) sb.AppendLine("  " + l);
        sb.AppendLine("=== STUB-LOCAL REQUESTS ===");
        foreach (var l in aborted) sb.AppendLine("  " + l);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "qs_debug_dump.txt"), sb.ToString());

        throw new Exception("diagnostic only");
    }
}