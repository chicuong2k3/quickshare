using LightResults;
using QuickShare.Infrastructure;

namespace QuickShare.Features.FileRequests.Create;

public record CreateFileRequestCommand(string Title, string? Message, string? Kind = null) : IRequest<Result<CreateFileRequestResponse>>;

public record CreateFileRequestResponse(string Code, string Title, DateTime ExpiresAt, string Kind);

public sealed class CreateFileRequestHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<CreateFileRequestCommand, Result<CreateFileRequestResponse>>
{
    public async ValueTask<Result<CreateFileRequestResponse>> Handle(
        CreateFileRequestCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var plan = user.Plan;

        var now = DateTime.UtcNow;
        var request = new FileRequest
        {
            OwnerId = user.Id,
            Code = ShareCodes.Generate(),
            Title = cmd.Title,
            Message = cmd.Message,
            MaxTotalBytes = PlanTierLimits.GetRequestMaxTotalBytes(plan) ?? 0,
            MaxFiles = PlanTierLimits.GetRequestMaxFiles(plan),
            ExpiresAt = now.Add(PlanTierLimits.GetRequestMaxExpiry(plan)),
            // A room is the same object with one difference that matters: everybody who has
            // the link sees what everybody dropped (issue #17). Anything unrecognised is a
            // request, because the private shape is the safe default.
            Kind = cmd.Kind == FileRequestKind.Room ? FileRequestKind.Room : FileRequestKind.Request,
        };

        db.FileRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateFileRequestResponse(request.Code, request.Title, request.ExpiresAt, request.Kind));
    }
}
