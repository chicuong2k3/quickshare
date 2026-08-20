namespace QuickShare.Features.Users.Retention;

public sealed class UpdateRetentionPolicyRequest
{
    /// <summary>Retention window in days (1–365); null clears the policy.</summary>
    public int? RetentionDays { get; set; }

    /// <summary>When true, extends may never push a share past the window.</summary>
    public bool NoExtend { get; set; }
}

/// <summary>PUT /api/account/retention — set or clear the account retention window (issue #161).</summary>
public sealed class UpdateRetentionPolicyEndpoint : Endpoint<UpdateRetentionPolicyRequest>
{
    public override void Configure()
    {
        Put("/api/account/retention");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Set or clear the account-wide retention window";
            s.Description = "Paid plans only (issue #161). Every share the account creates expires within the window: uploads and extends clamp to it, and the expiry sweep converges older rows onto it. Null clears the policy.";
        });
    }

    public override async Task HandleAsync(UpdateRetentionPolicyRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<UpdateRetentionPolicyResponse>>(
            new UpdateRetentionPolicyCommand(req.RetentionDays, req.NoExtend), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.RequiresPaidPlan => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            });
    }
}
