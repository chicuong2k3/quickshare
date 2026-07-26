using System.Text.Json;
using LightResults;

namespace QuickShare.Extensions;

public static class EndpointMessageBusExtensions
{
    public static async Task SendResultOkAsync<TRequest, TResponse, TValue>(
        this Endpoint<TRequest, TResponse> ep,
        Task<Result<TValue>> resultTask,
        CancellationToken ct)
        where TRequest : notnull
    {
        var result = await resultTask;
        if (result.IsSuccess(out var value))
        {
            await ep.HttpContext.Response.SendAsync(value, 200, null, ct);
        }
        else if (result.IsFailure(out var error))
        {
            var statusCode = error.ToHttpStatusCode();
            ep.HttpContext.Response.StatusCode = statusCode;
            await ep.HttpContext.Response.WriteAsJsonAsync(new { errors = new[] { error.Message } }, ct);
        }
    }

    public static async Task SendResultNoContentAsync<TRequest>(
        this Endpoint<TRequest> ep,
        Task<Result> resultTask,
        CancellationToken ct)
        where TRequest : notnull
    {
        var result = await resultTask;
        if (result.IsSuccess())
        {
            await ep.HttpContext.Response.SendNoContentAsync(ct);
        }
        else if (result.IsFailure(out var error))
        {
            var statusCode = error.ToHttpStatusCode();
            ep.HttpContext.Response.StatusCode = statusCode;
            await ep.HttpContext.Response.WriteAsJsonAsync(new { errors = new[] { error.Message } }, ct);
        }
    }
}
