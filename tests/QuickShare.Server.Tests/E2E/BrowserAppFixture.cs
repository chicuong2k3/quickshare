using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Playwright needs a real HTTP origin — clipboard, sessionStorage and page
/// navigation do not exist inside <c>WebApplicationFactory</c>'s in-memory
/// handler — so this boots the app as a process on a free port with the
/// in-memory database, and drives the Chrome already installed on the machine
/// rather than downloading a browser bundle.
/// </summary>
public sealed class BrowserAppFixture : IAsyncLifetime
{
    private const string ChromeChannel = "chrome";
    private const int StartupTimeoutSeconds = 90;

    private readonly System.Text.StringBuilder _log = new();
    private Process? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Null when no usable Chrome is installed; tests skip themselves.</summary>
    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var projectPath = Path.Combine(RepoRoot(), "src", "QuickShare", "QuickShare.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --no-build --no-launch-profile --project \"{projectPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Without this the app inherits the test bin directory and never finds
            // appsettings.json, so it dies before it can serve anything.
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["UseInMemoryDatabase"] = "true";
        startInfo.Environment["Authentication__Google__ClientId"] = "test-client-id";
        startInfo.Environment["Authentication__Google__ClientSecret"] = "test-client-secret";

        try
        {
            _app = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Unavailable = $"could not start the app: {ex.Message}";
            return;
        }

        _app.OutputDataReceived += (_, e) => { if (e.Data is not null) _log.AppendLine(e.Data); };
        _app.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log.AppendLine(e.Data); };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();

        if (!await WaitForServerAsync(port))
        {
            Unavailable = $"the app did not start listening in time. Output: {_log}";
            return;
        }

        try
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = ChromeChannel,
                Headless = true,
            });
        }
        catch (Exception ex)
        {
            // A machine with no Chrome channel is a missing capability, not a
            // failing product: the suite says so out loud instead of going red.
            Unavailable = $"no usable Chrome for Playwright: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();

        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            await _app.WaitForExitAsync();
        }

        _app?.Dispose();
    }

    private async Task<bool> WaitForServerAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (_app is { HasExited: true })
                return false;

            try
            {
                // A socket that accepts is not the same as an app that answers.
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/");
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(500);
        }

        return false;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "QuickShare.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
