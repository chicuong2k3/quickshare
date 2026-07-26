using LightResults;

namespace QuickShare.Extensions;

public static class ResultExtensions
{
    /// <summary>
    /// Maps a LightResults <see cref="IError"/> to an HTTP status code.
    /// Use with FastEndpoints' <c>ThrowError(message, statusCode)</c>.
    /// </summary>
    public static int ToHttpStatusCode(this IError error) => error switch
    {
        NotFoundError => 404,
        ForbiddenError => 403,
        BadRequestError => 400,
        ValidationError => 422,
        ConflictError => 409,
        _ => 500
    };
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess())
            return Results.NoContent();

        if (result.IsFailure(out var error))
        {
            return error switch
            {
                NotFoundError notFound => Results.NotFound(new { error = notFound.Message }),
                BadRequestError bad => Results.BadRequest(new { error = bad.Message }),
                ValidationError val => Results.UnprocessableEntity(new { error = val.Message }),
                ConflictError conflict => Results.Conflict(new { error = conflict.Message }),
                ForbiddenError forbidden => Results.Forbid(),
                _ => Results.InternalServerError()
            };
        }

        return Results.InternalServerError();
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess(out var value))
            return Results.Ok(value);

        if (result.IsFailure(out var error))
        {
            return error switch
            {
                NotFoundError notFound => Results.NotFound(new { error = notFound.Message }),
                BadRequestError bad => Results.BadRequest(new { error = bad.Message }),
                ValidationError val => Results.UnprocessableEntity(new { error = val.Message }),
                ConflictError conflict => Results.Conflict(new { error = conflict.Message }),
                ForbiddenError forbidden => Results.Forbid(),
                _ => Results.InternalServerError()
            };
        }

        return Results.InternalServerError();
    }
}
