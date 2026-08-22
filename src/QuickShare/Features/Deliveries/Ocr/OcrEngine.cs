using System.Diagnostics;
using System.Text;

namespace QuickShare.Features.Deliveries.Ocr;

/// <summary>Reads text out of an image, or gives up (issue #243).</summary>
public interface IOcrEngine
{
    /// <summary>False when the deployment has no OCR binary — the toggle is then never offered.</summary>
    bool IsAvailable { get; }

    /// <summary>Recognised text, or null when nothing could be read.</summary>
    Task<string?> ReadAsync(Stream image, string languages, CancellationToken ct);
}

/// <summary>
/// Tesseract, driven as a subprocess (issue #243).
///
/// ponytail: the installed binary, not the interop package. The NuGet wrapper drags native
/// leptonica and tesseract libraries per platform and pins their versions to ours; a process is
/// a documented apt line in the Dockerfile and nothing in the build. The ceiling is one image
/// per process launch — fine, because this runs from a background job over files that were just
/// uploaded, not on a request path. If throughput ever matters, the upgrade is a persistent
/// worker, not a different library.
///
/// **Nothing leaves the machine.** That is the reason for Tesseract over a cloud vision API: the
/// privacy page says we do not ship your files to third parties, and an OCR feature that did
/// would quietly make that untrue.
/// </summary>
public sealed class TesseractOcrEngine(ILogger<TesseractOcrEngine> logger) : IOcrEngine
{
    public const string Binary = "tesseract";
    public const string DefaultLanguages = "eng+vie";
    /// <summary>A page of text takes a second or two; anything past this is a stuck process.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Lazy<bool> _available = new(() => Probe(logger));

    public bool IsAvailable => _available.Value;

    public async Task<string?> ReadAsync(Stream image, string languages, CancellationToken ct)
    {
        if (!IsAvailable)
            return null;

        // A temp file rather than stdin: tesseract's stdin path needs the image format declared
        // up front, and the extension it sniffs is free.
        var input = Path.Combine(Path.GetTempPath(), $"qs-ocr-{Guid.CreateVersion7():N}");

        try
        {
            await using (var file = File.Create(input))
            {
                await image.CopyToAsync(file, ct);
            }

            return await RunAsync(input, languages, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "OCR could not stage its input");
            return null;
        }
        finally
        {
            TryDelete(input);
        }
    }

    private async Task<string?> RunAsync(string input, string languages, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add(input);
        // "-" writes the text to stdout, which saves managing an output file as well.
        start.ArgumentList.Add("-");
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add(string.IsNullOrWhiteSpace(languages) ? DefaultLanguages : languages);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            if (process.ExitCode != 0)
            {
                logger.LogInformation("OCR exited {Code}", process.ExitCode);
                return null;
            }

            var text = output.Trim();
            return text.Length == 0 ? null : text;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("OCR timed out after {Seconds}s", Timeout.TotalSeconds);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "OCR process failed");
            return null;
        }
    }

    /// <summary>One probe per process. A missing binary is a deployment fact, not a per-file one.</summary>
    private static bool Probe(ILogger logger)
    {
        try
        {
            var start = new ProcessStartInfo(Binary)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--version");

            using var process = Process.Start(start);
            if (process is null)
                return false;

            process.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogInformation(ex, "OCR is unavailable on this host");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The temp sweep owns whatever is left behind.
        }
    }

    /// <summary>Trims recognised text to what is worth storing and searching.</summary>
    public static string Normalize(string text, int maxLength)
    {
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        var lastWasSpace = false;

        foreach (var c in text)
        {
            var current = char.IsWhiteSpace(c) ? ' ' : c;
            if (current == ' ' && lastWasSpace)
                continue;

            lastWasSpace = current == ' ';
            builder.Append(current);

            if (builder.Length >= maxLength)
                break;
        }

        return builder.ToString().Trim();
    }
}
