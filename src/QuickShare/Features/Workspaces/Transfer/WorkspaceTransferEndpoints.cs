using LightResults;
using QuickShare.Features.Workspaces.Manage;

namespace QuickShare.Features.Workspaces.Transfer;

public sealed class InitiateTransferRequest
{
    public Guid MemberId { get; set; }
}

/// <summary>POST /api/workspace/transfer — nominate a successor owner (issue #252).</summary>
public sealed class InitiateTransferEndpoint : Endpoint<InitiateTransferRequest, TransferDto>
{
    public override void Configure()
    {
        Post("/api/workspace/transfer");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Nominate a member as the next workspace owner";
            s.Description = "Owner only. Nothing moves until the successor accepts inside a 7-day window, because ownership carries the bill.";
        });
    }

    public override async Task HandleAsync(InitiateTransferRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TransferDto>>(
            new InitiateTransferCommand(req.MemberId), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

public sealed class AcceptTransferRequest
{
    public string? Token { get; set; }
}

/// <summary>POST /api/workspace/transfer/accept — take ownership (issue #252).</summary>
public sealed class AcceptTransferEndpoint : Endpoint<AcceptTransferRequest, TransferDto>
{
    public override void Configure()
    {
        Post("/api/workspace/transfer/accept");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Accept workspace ownership";
            s.Description = "The nominated member only. Ownership and billing move; the previous owner is demoted to admin so the workspace is never left unadministered.";
        });
    }

    public override async Task HandleAsync(AcceptTransferRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TransferDto>>(
            new AcceptTransferCommand(req.Token), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

/// <summary>POST /api/workspace/transfer/cancel — withdraw an open nomination (issue #252).</summary>
public sealed class CancelTransferEndpoint : EndpointWithoutRequest<TransferDto>
{
    public override void Configure()
    {
        Post("/api/workspace/transfer/cancel");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Cancel the open ownership transfer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TransferDto>>(new CancelTransferCommand(), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}
