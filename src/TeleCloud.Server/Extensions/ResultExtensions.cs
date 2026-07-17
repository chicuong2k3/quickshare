using LightResults;

namespace TeleCloud.Server.Extensions;

public static class ResultExtensions
{
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
                _ => Results.InternalServerError()
            };
        }

        return Results.InternalServerError();
    }
}
