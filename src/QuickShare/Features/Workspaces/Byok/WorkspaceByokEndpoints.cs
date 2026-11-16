using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Byok;

public sealed record WorkspaceByokDto(
    bool Configured,
    string? Provider,
    string? KeyId,
    string? RoleArn,
    string? KeyRegion,
    int KeyVersion,
    string Status,
    DateTime? LastRotatedAt,
    DateTime? RevokedAt);

public sealed record ConfigureWorkspaceByokRequest(
    string Provider,
    string KeyId,
    string? RoleArn,
    string? KeyRegion,
    string? Status);

/// <summary>GET /api/workspace/byok — inspect workspace BYOK KMS configuration (issue #163).</summary>
public sealed class GetWorkspaceByokEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : EndpointWithoutRequest<WorkspaceByokDto>
{
    public override void Configure()
    {
        Get(RoutePaths.WorkspaceByokApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }
        if (!seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var cfg = await db.WorkspaceByokConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == seat.WorkspaceId, ct);

        if (cfg is null)
        {
            await Send.OkAsync(new WorkspaceByokDto(
                false, null, null, null, null, 0, WorkspaceByokConfig.StatusDisabled, null, null), ct);
            return;
        }

        await Send.OkAsync(MapDto(cfg), ct);
    }

    internal static WorkspaceByokDto MapDto(WorkspaceByokConfig c) => new(
        true,
        c.Provider,
        c.KeyId,
        c.RoleArn,
        c.KeyRegion,
        c.KeyVersion,
        c.Status,
        c.LastRotatedAt,
        c.RevokedAt);

    internal static string WrapEnvelopeKey(string keyId, int keyVersion, Guid deliveryId)
    {
        var payload = $"byok:v{keyVersion}:{keyId}:{deliveryId:N}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"kek-v{keyVersion}:{Convert.ToBase64String(digest)}";
    }
}

/// <summary>PUT /api/workspace/byok — configure customer-managed KMS key for Studio workspace (issue #163).</summary>
public sealed class ConfigureWorkspaceByokEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : Endpoint<ConfigureWorkspaceByokRequest, WorkspaceByokDto>
{
    public override void Configure()
    {
        Put(RoutePaths.WorkspaceByokApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(ConfigureWorkspaceByokRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }
        if (!seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }
        if (seat.Workspace.Owner.Plan != PlanTierLimits.Studio)
        {
            ThrowError(ErrorCodes.ByokRequiresStudio, StatusCodes.Status403Forbidden);
            return;
        }

        var provider = req.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        var keyId = req.KeyId?.Trim() ?? string.Empty;
        if (!WorkspaceByokConfig.ValidProviders.Contains(provider) ||
            string.IsNullOrWhiteSpace(keyId) ||
            keyId.Length > WorkspaceByokConfig.KeyIdMaxLength)
        {
            ThrowError(ErrorCodes.ByokConfigInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var region = string.IsNullOrWhiteSpace(req.KeyRegion)
            ? null
            : req.KeyRegion.Trim().ToLowerInvariant();

        var existing = await db.WorkspaceByokConfigs
            .FirstOrDefaultAsync(x => x.WorkspaceId == seat.WorkspaceId, ct);

        if (existing is null)
        {
            existing = new WorkspaceByokConfig
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                KeyVersion = 1,
            };
            db.WorkspaceByokConfigs.Add(existing);
        }

        existing.Provider = provider;
        existing.KeyId = keyId;
        existing.RoleArn = string.IsNullOrWhiteSpace(req.RoleArn) ? null : req.RoleArn.Trim();
        existing.KeyRegion = region;
        existing.Status = string.Equals(req.Status, WorkspaceByokConfig.StatusDisabled, StringComparison.OrdinalIgnoreCase)
            ? WorkspaceByokConfig.StatusDisabled
            : WorkspaceByokConfig.StatusActive;
        if (existing.Status == WorkspaceByokConfig.StatusActive)
            existing.RevokedAt = null;

        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(
            seat.WorkspaceId,
            user.Id,
            SecurityEventType.ByokConfigChanged,
            HttpContext,
            ct,
            JsonSerializer.Serialize(new { existing.Provider, existing.KeyId, existing.KeyRegion, existing.KeyVersion, existing.Status }));

        await Send.OkAsync(GetWorkspaceByokEndpoint.MapDto(existing), ct);
    }
}

/// <summary>POST /api/workspace/byok/rotate — increment customer KEK version (issue #163).</summary>
public sealed class RotateWorkspaceByokEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : EndpointWithoutRequest<WorkspaceByokDto>
{
    public override void Configure()
    {
        Post(RoutePaths.WorkspaceByokRotateApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }
        if (!seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var existing = await db.WorkspaceByokConfigs
            .FirstOrDefaultAsync(x => x.WorkspaceId == seat.WorkspaceId, ct);
        if (existing is null)
        {
            ThrowError(ErrorCodes.ByokConfigInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        existing.KeyVersion++;
        existing.LastRotatedAt = DateTime.UtcNow;
        existing.Status = WorkspaceByokConfig.StatusActive;
        existing.RevokedAt = null;
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(
            seat.WorkspaceId,
            user.Id,
            SecurityEventType.ByokKeyRotated,
            HttpContext,
            ct,
            JsonSerializer.Serialize(new { existing.KeyId, existing.KeyVersion }));

        await Send.OkAsync(GetWorkspaceByokEndpoint.MapDto(existing), ct);
    }
}

/// <summary>POST /api/workspace/byok/revoke — revoke customer KEK so envelope-encrypted deliveries cannot be decrypted (issue #163).</summary>
public sealed class RevokeWorkspaceByokEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : EndpointWithoutRequest<WorkspaceByokDto>
{
    public override void Configure()
    {
        Post(RoutePaths.WorkspaceByokRevokeApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceNotFound, StatusCodes.Status404NotFound);
            return;
        }
        if (!seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var existing = await db.WorkspaceByokConfigs
            .FirstOrDefaultAsync(x => x.WorkspaceId == seat.WorkspaceId, ct);
        if (existing is null)
        {
            ThrowError(ErrorCodes.ByokConfigInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        existing.Status = WorkspaceByokConfig.StatusRevoked;
        existing.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(
            seat.WorkspaceId,
            user.Id,
            SecurityEventType.ByokKeyRevoked,
            HttpContext,
            ct,
            JsonSerializer.Serialize(new { existing.KeyId, existing.KeyVersion, existing.RevokedAt }));

        await Send.OkAsync(GetWorkspaceByokEndpoint.MapDto(existing), ct);
    }
}
