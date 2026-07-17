using LightResults;

namespace TeleCloud.Server.Features.Files.Shared;

public sealed class NotFoundError : Error
{
    public NotFoundError(string entity, Guid id)
        : base($"{entity} with id '{id}' was not found.")
    {
    }
}

public sealed class BadRequestError : Error
{
    public BadRequestError(string message) : base(message) { }
}

public sealed class ValidationError : Error
{
    public ValidationError(string message) : base(message) { }
}

public sealed class ConflictError : Error
{
    public ConflictError(string message) : base(message) { }
}
