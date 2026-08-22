

using QuickShare.Features.Uploads.Shared;


namespace QuickShare.Features.Uploads.CommitR2;

public sealed class CommitR2Endpoint : Endpoint<CommitR2Request, CommitR2Response>
{
    public override void Configure()
    {
        Post("/api/uploads/commit-r2");
        AllowAnonymous();
        Validator<CommitR2Validator>();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Complete an R2 multipart upload";
            s.Description = "Completes the R2 multipart upload with provided part ETags and creates a Delivery record. Returns the share link.";
            s.Responses[201] = "Delivery created. Returns share code and link.";
            s.Responses[400] = "Validation error.";
            s.Responses[502] = "R2 upstream failure.";
        });
    }

    public override async Task HandleAsync(CommitR2Request req, CancellationToken ct)
    {
        var cmd = new CommitR2Command(
            req.DeliveryId, req.UploadId, req.ObjectKey,
            req.FileName, req.ContentType, req.FileSize, req.Parts, req.Otp, req.Password, req.Message, req.Source, req.DeliverAt, req.ExpiryHours, req.Sha256, req.BurnAfterDownload, req.AllowExtensionRequests, req.RecipientEmails, req.PrivacyMode, req.EmailOtpEnabled, req.RequireRecipientEmail, req.PublicStatsEnabled, req.AllowedIpRanges, req.AllowedCountries, req.BlockedCountries, req.FolderId, req.CommentsEnabled, req.WatermarkEnabled,
            req.AdditionalFiles?.Select(f => new CommitFile(
                f.ObjectKey, f.UploadId, f.FileName, f.ContentType, f.FileSize, f.Parts, f.Sha256)).ToList());
        var result = await Resolve<IMediator>().Send<LightResults.Result<CommitR2Response>>(cmd, ct);

        if (result.IsSuccess(out var value))
        {
            HttpContext.Response.Headers.Location = $"/api/downloads/{value.ShareCode}";
            await Send.ResponseAsync(value, StatusCodes.Status201Created, ct);
        }
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message.StartsWith(UploadErrors.R2Upstream, StringComparison.Ordinal) ? 502 : 400);
    }
}

public class CommitR2Request
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public IReadOnlyList<R2PartEtag> Parts { get; set; } = [];
    public string? Otp { get; set; }
    public string? Password { get; set; }
    public string? Message { get; set; }
    public string? Source { get; set; }
    public DateTime? DeliverAt { get; set; }
    public double? ExpiryHours { get; set; }
    public string? Sha256 { get; set; }
    public bool BurnAfterDownload { get; set; }
    public bool AllowExtensionRequests { get; set; }
    public List<string>? RecipientEmails { get; set; }
    public bool PrivacyMode { get; set; }
    public bool EmailOtpEnabled { get; set; }
    public bool RequireRecipientEmail { get; set; }
    public bool PublicStatsEnabled { get; set; }
    /// <summary>IP allowlist (issue #142): comma-separated CIDR ranges. Pro/Studio only.</summary>
    public string? AllowedIpRanges { get; set; }
    /// <summary>Country allow-list (issue #166): comma-separated ISO 3166-1 alpha-2 codes. Pro/Studio only.</summary>
    public string? AllowedCountries { get; set; }
    /// <summary>Country deny-list (issue #166): comma-separated ISO 3166-1 alpha-2 codes. Pro/Studio only.</summary>
    public string? BlockedCountries { get; set; }
    /// <summary>Shared workspace folder to file this delivery under (issue #151); null = loose in the workspace.</summary>
    public Guid? FolderId { get; set; }
    /// <summary>Let recipients and the team discuss this share (issue #289). Off by default.</summary>
    public bool CommentsEnabled { get; set; }
    /// <summary>Overlay the sender's name across the in-page preview (issue #155). Pro/Studio only.</summary>
    public bool WatermarkEnabled { get; set; }

    /// <summary>
    /// Files beyond the first, each already uploaded via <c>POST /api/uploads/{id}/files</c>
    /// (issue #79). Absent or empty means a single-file delivery, which behaves exactly as it
    /// always has.
    /// </summary>
    public List<CommitFileRequest>? AdditionalFiles { get; set; }
}

/// <summary>One extra file of a bundle, as posted at commit (issue #79).</summary>
public sealed class CommitFileRequest
{
    public string ObjectKey { get; set; } = string.Empty;
    public string UploadId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public IReadOnlyList<R2PartEtag> Parts { get; set; } = [];
    public string? Sha256 { get; set; }
}
