using System.Diagnostics;

namespace QuickShare.Infrastructure.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        // WebSocket requests carry no response body; buffering here would also
        // break the TestHost handshake flush that signals "response started".
        if (ctx.WebSockets.IsWebSocketRequest)
        {
            await next(ctx);
            return;
        }

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
            var status = ctx.Response.StatusCode;
            var canHaveBody = status != StatusCodes.Status204NoContent
                              && status != StatusCodes.Status304NotModified;

            string responseBody;
            if (canHaveBody)
            {
                buffer.Seek(0, SeekOrigin.Begin);
                responseBody = await new StreamReader(buffer).ReadToEndAsync();
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
            }
            else
            {
                responseBody = string.Empty;
            }
            ctx.Response.Body = originalBody;

            var method = ctx.Request.Method;
            var path = ctx.Request.Path + ctx.Request.QueryString;
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
