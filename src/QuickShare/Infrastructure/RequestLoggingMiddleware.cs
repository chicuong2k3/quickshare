using System.Diagnostics;

namespace QuickShare.Infrastructure;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var originalBody = ctx.Response.Body;

        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await next(ctx);
        }
        finally
        {
            sw.Stop();
            buffer.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(buffer).ReadToEndAsync();
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            ctx.Response.Body = originalBody;

            var method = ctx.Request.Method;
            var path = ctx.Request.Path + ctx.Request.QueryString;
            var status = ctx.Response.StatusCode;
            var elapsed = sw.ElapsedMilliseconds;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;

            var responsePreview = responseBody.Length > 2000
                ? responseBody[..2000] + "...[truncated]"
                : responseBody;

            logger.Log(level,
                "{Method} {Path} → {StatusCode} ({Elapsed}ms)\nResponse: {Response}",
                method, path, status, elapsed, responsePreview);
        }
    }
}
