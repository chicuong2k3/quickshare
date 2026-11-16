using System.Text.Json;
using System.Text.Json.Serialization;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Auth.Scim;

/// <summary>
/// SCIM 2.0 schema URNs and constants (RFC 7643/7644).
///
/// Identity providers validate these strings, so they are not decoration: Entra rejects a
/// response whose `schemas` array it does not recognise.
/// </summary>
public static class ScimSchemas
{
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string ListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    public const string ContentType = "application/scim+json";
    public const string ResourceType = "User";
    public const int MaxPageSize = 100;
}

public sealed record ScimName(
    [property: JsonPropertyName("formatted")] string? Formatted);

public sealed record ScimEmail(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("primary")] bool Primary);

public sealed record ScimMeta(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("created")] DateTime Created,
    [property: JsonPropertyName("lastModified")] DateTime LastModified);

/// <summary>
/// A workspace seat as SCIM sees it (issue #146).
///
/// <c>active</c> is the field the whole feature turns on: an identity provider offboards by
/// PATCHing it to false, and that must revoke the seat. It maps to
/// <see cref="WorkspaceMember.RevokedAt"/> — the row survives so the audit log stays whole.
/// </summary>
public sealed record ScimUser(
    [property: JsonPropertyName("schemas")] IReadOnlyList<string> Schemas,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("name")] ScimName? Name,
    [property: JsonPropertyName("emails")] IReadOnlyList<ScimEmail> Emails,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("meta")] ScimMeta Meta);

public sealed record ScimListResponse(
    [property: JsonPropertyName("schemas")] IReadOnlyList<string> Schemas,
    [property: JsonPropertyName("totalResults")] int TotalResults,
    [property: JsonPropertyName("startIndex")] int StartIndex,
    [property: JsonPropertyName("itemsPerPage")] int ItemsPerPage,
    [property: JsonPropertyName("Resources")] IReadOnlyList<ScimUser> Resources);

public sealed class ScimUserRequest
{
    public string? UserName { get; set; }
    public string? ExternalId { get; set; }
    public bool Active { get; set; } = true;
    public ScimName? Name { get; set; }
    public List<ScimEmail>? Emails { get; set; }
}

public sealed class ScimPatchRequest
{
    public List<string>? Schemas { get; set; }
    public List<ScimPatchOperation>? Operations { get; set; }
}

public sealed class ScimPatchOperation
{
    public string? Op { get; set; }
    public string? Path { get; set; }
    public JsonElement Value { get; set; }
}

/// <summary>
/// Resolves the workspace behind a SCIM bearer token (issue #146).
///
/// SCIM is machine-to-machine — there is no cookie and no signed-in user, so authorisation is
/// the token and nothing else. Every endpoint runs through here, and every query it produces is
/// scoped to the returned workspace id: the token is the tenant boundary (issue #190).
/// </summary>
public sealed class ScimAuthenticator(AppDbContext db)
{
    private const string BearerPrefix = "Bearer ";

    public async Task<Guid?> ResolveWorkspaceAsync(HttpContext context, CancellationToken ct)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var raw = header[BearerPrefix.Length..].Trim();
        if (raw.Length == 0)
            return null;

        // Looked up by hash, so a database dump does not hand anybody a working credential.
        var hash = WorkspaceService.HashToken(raw);
        var token = await db.WorkspaceScimTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);

        if (token is null)
            return null;

        // Last-used is what tells an admin whether a token is still driving anything before
        // they revoke it. Written on every call; SCIM traffic is low-volume by nature.
        token.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return token.WorkspaceId;
    }
}

/// <summary>
/// GET /scim/v2/Users — list or filter seats (issue #146).
///
/// Supports the one filter shape identity providers actually send during provisioning:
/// <c>userName eq "someone@acme.com"</c>. Okta and Entra both use it to decide whether to POST a
/// new user or PATCH an existing one. Anything more elaborate returns the unfiltered page rather
/// than an error, because a half-understood filter silently dropping rows is worse than ignoring
/// it — the provider would read an empty list as "not provisioned" and create a duplicate.
/// </summary>
public sealed class ScimListUsersEndpoint : EndpointWithoutRequest<ScimListResponse>
{
    public override void Configure()
    {
        Get("/scim/v2/Users");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Scim));
        Summary(s =>
        {
            s.Summary = "SCIM 2.0: list provisioned members";
            s.Description = "Bearer token auth. Supports filter=userName eq \"...\".";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaceId = await Resolve<ScimAuthenticator>().ResolveWorkspaceAsync(HttpContext, ct);
        if (workspaceId is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var query = db.WorkspaceMembers.AsNoTracking().Where(m => m.WorkspaceId == workspaceId.Value);

        if (ScimFilter.TryReadUserName(Query<string>("filter", isRequired: false), out var userName))
            query = query.Where(m => m.InviteEmail == userName);

        var startIndex = Math.Max(1, Query<int?>("startIndex", isRequired: false) ?? 1);
        var count = Math.Clamp(Query<int?>("count", isRequired: false) ?? ScimSchemas.MaxPageSize, 1, ScimSchemas.MaxPageSize);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(m => m.CreatedAt)
            .Skip(startIndex - 1)
            .Take(count)
            .Select(m => new { Member = m, Name = db.Users.Where(u => u.Id == m.UserId).Select(u => u.Name).FirstOrDefault() })
            .ToListAsync(ct);

        var resources = rows.Select(r => ScimMapper.Map(r.Member, r.Name)).ToList();

        HttpContext.Response.ContentType = ScimSchemas.ContentType;
        await Send.OkAsync(new ScimListResponse(
            [ScimSchemas.ListResponse], total, startIndex, resources.Count, resources), ct);
    }
}

/// <summary>GET /scim/v2/Users/{Id} — read one seat (issue #146).</summary>
public sealed class ScimGetUserEndpoint : EndpointWithoutRequest<ScimUser>
{
    public override void Configure()
    {
        Get("/scim/v2/Users/{Id}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Scim));
        Summary(s => s.Summary = "SCIM 2.0: read one provisioned member");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaceId = await Resolve<ScimAuthenticator>().ResolveWorkspaceAsync(HttpContext, ct);
        if (workspaceId is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var member = await ScimMapper.FindAsync(db, workspaceId.Value, Route<Guid>("Id"), ct);
        if (member is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status404NotFound, ct);
            return;
        }

        HttpContext.Response.ContentType = ScimSchemas.ContentType;
        await Send.OkAsync(ScimMapper.Map(member, await ScimMapper.NameAsync(db, member.UserId, ct)), ct);
    }
}

/// <summary>
/// POST /scim/v2/Users — provision a seat (issue #146).
///
/// Idempotent on <c>userName</c>: a provider that re-posts an existing user gets the existing
/// seat back with 200 rather than a duplicate. Okta does exactly this when a sync is re-run, and
/// creating a second seat for one person would silently eat a paid seat.
///
/// The account itself is not created here — a workspace seat can exist before its person has
/// ever signed in, which is what the pending-invite row already models (#106). SCIM fills that
/// row in; the first SSO sign-in claims it.
/// </summary>
public sealed class ScimCreateUserEndpoint : Endpoint<ScimUserRequest, ScimUser>
{
    public override void Configure()
    {
        Post("/scim/v2/Users");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Scim));
        Summary(s =>
        {
            s.Summary = "SCIM 2.0: provision a member";
            s.Description = "Idempotent on userName. Refused with 409 when the workspace has no free seat.";
        });
    }

    public override async Task HandleAsync(ScimUserRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaceId = await Resolve<ScimAuthenticator>().ResolveWorkspaceAsync(HttpContext, ct);
        if (workspaceId is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var email = (req.UserName ?? req.Emails?.FirstOrDefault()?.Value ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0 || email.Length > WorkspaceMember.EmailMaxLength)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var existing = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId.Value && m.InviteEmail == email, ct);

        if (existing is not null)
        {
            // Re-provisioning a revoked seat is a rehire — the provider is the system of record.
            if (existing.RevokedAt is not null && req.Active)
            {
                existing.RevokedAt = null;
                await AuditAsync(db, workspaceId.Value, existing, SecurityEventType.ScimUserProvisioned, ct);
                await db.SaveChangesAsync(ct);
            }

            HttpContext.Response.ContentType = ScimSchemas.ContentType;
            await Send.OkAsync(ScimMapper.Map(existing, await ScimMapper.NameAsync(db, existing.UserId, ct)), ct);
            return;
        }

        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workspaceId.Value, ct);
        var held = await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId.Value && m.RevokedAt == null, ct);

        if (workspace is null || held >= workspace.Seats)
        {
            // 409 rather than a silent no-op: the provider surfaces it to the IT admin, who is
            // the only person who can buy another seat.
            await Send.ResponseAsync(null!, StatusCodes.Status409Conflict, ct);
            return;
        }

        // Matched to an existing account when one already holds this address, so an SSO sign-in
        // that happened before provisioning does not end up with two rows.
        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        var member = new WorkspaceMember
        {
            WorkspaceId = workspaceId.Value,
            InviteEmail = email,
            UserId = userId,
            Role = WorkspaceRole.Member,
            JoinedAt = userId is null ? null : DateTime.UtcNow,
            InviteExpiresAt = userId is null ? DateTime.UtcNow.AddDays(WorkspaceMember.InviteValidDays) : null,
        };

        db.WorkspaceMembers.Add(member);
        await AuditAsync(db, workspaceId.Value, member, SecurityEventType.ScimUserProvisioned, ct);
        await db.SaveChangesAsync(ct);

        if (userId is not null)
            await new WorkspaceService(db).SyncMemberPlansAsync(workspaceId.Value, ct);

        HttpContext.Response.ContentType = ScimSchemas.ContentType;
        await Send.ResponseAsync(ScimMapper.Map(member, await ScimMapper.NameAsync(db, member.UserId, ct)),
            StatusCodes.Status201Created, ct);
    }

    /// <summary>
    /// Records a provisioning decision on the workspace timeline (#145).
    ///
    /// SCIM has no signed-in actor, and a SCIM seat can exist before its person has ever signed
    /// in — a pending invite row has no account behind it. <see cref="SecurityEvent.UserId"/> is
    /// a foreign key to a real account, so the row is attributed to the seat holder when there is
    /// one and to the workspace owner otherwise. The owner is the accountable party either way,
    /// and the provisioned address is in the metadata, so the workspace lens stays complete
    /// rather than silently dropping every pending-seat event.
    /// </summary>
    internal static async Task AuditAsync(AppDbContext db, Guid workspaceId, WorkspaceMember member, SecurityEventType type, CancellationToken ct)
    {
        var subjectId = member.UserId ?? await db.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => (Guid?)w.OwnerId)
            .FirstOrDefaultAsync(ct);

        if (subjectId is null)
            return;

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = subjectId.Value,
            WorkspaceId = workspaceId,
            EventType = type,
            IpHash = string.Empty,
            UserAgentLabel = "scim",
            MetadataJson = JsonSerializer.Serialize(new { email = member.InviteEmail, onBehalfOf = member.UserId }),
            CreatedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>
/// PATCH /scim/v2/Users/{Id} — the offboarding path (issue #146).
///
/// This is the endpoint the whole feature exists for. When IT deactivates somebody in Okta or
/// Entra, the provider sends <c>active: false</c> here, and the seat must die: the plan the seat
/// borrowed is restored, and the audit log records it. Anything else means access outlives
/// employment, which is the exact question procurement asks.
/// </summary>
public sealed class ScimPatchUserEndpoint : Endpoint<ScimPatchRequest, ScimUser>
{
    private const string ActivePath = "active";

    public override void Configure()
    {
        Patch("/scim/v2/Users/{Id}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Scim));
        Summary(s =>
        {
            s.Summary = "SCIM 2.0: activate or deactivate a member";
            s.Description = "Deactivation revokes the seat and restores the member's own plan.";
        });
    }

    public override async Task HandleAsync(ScimPatchRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaceId = await Resolve<ScimAuthenticator>().ResolveWorkspaceAsync(HttpContext, ct);
        if (workspaceId is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var member = await ScimMapper.FindAsync(db, workspaceId.Value, Route<Guid>("Id"), ct);
        if (member is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status404NotFound, ct);
            return;
        }

        var active = ScimFilter.ReadActive(req.Operations);
        if (active is not null)
            await SetActiveAsync(db, workspaceId.Value, member, active.Value, ct);

        HttpContext.Response.ContentType = ScimSchemas.ContentType;
        await Send.OkAsync(ScimMapper.Map(member, await ScimMapper.NameAsync(db, member.UserId, ct)), ct);
    }

    internal static async Task SetActiveAsync(
        AppDbContext db, Guid workspaceId, WorkspaceMember member, bool active, CancellationToken ct)
    {
        // The owner's seat is not the provider's to close: it carries the subscription, and
        // deactivating it would leave a paid workspace with nobody able to administer it.
        if (member.Role == WorkspaceRole.Owner)
            return;

        if (active && member.RevokedAt is not null)
        {
            member.RevokedAt = null;
            await ScimCreateUserEndpoint.AuditAsync(db, workspaceId, member, SecurityEventType.ScimUserProvisioned, ct);
        }
        else if (!active && member.RevokedAt is null)
        {
            member.RevokedAt = DateTime.UtcNow;
            await ScimCreateUserEndpoint.AuditAsync(db, workspaceId, member, SecurityEventType.ScimUserDeprovisioned, ct);
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(ct);

        // Restores the member's own plan when the borrowed seat goes away (#106).
        var workspaces = new WorkspaceService(db);
        if (member.UserId is { } userId && !active)
        {
            await workspaces.RestorePersonalPlanAsync(userId, ct);

            // Cut their sessions too (#105). Until the session store existed this was documented
            // as a known gap: the seat died immediately but a signed-in browser kept working
            // until its cookie expired, which is not what "IT deactivated them" is supposed to
            // mean. Every session, not all-but-current — SCIM has no current session.
            await new Users.Sessions.SessionService(db).RevokeAllAsync(userId, null, ct);
        }
        else
            await workspaces.SyncMemberPlansAsync(workspaceId, ct);
    }
}

/// <summary>
/// DELETE /scim/v2/Users/{Id} — offboarding for providers that delete rather than deactivate.
///
/// Treated as deactivation, not deletion: the seat row is the audit trail (#145), and a provider
/// that removes an assignment has said "this person no longer has access", not "erase the record
/// that they ever did".
/// </summary>
public sealed class ScimDeleteUserEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/scim/v2/Users/{Id}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Scim));
        Summary(s =>
        {
            s.Summary = "SCIM 2.0: deprovision a member";
            s.Description = "Revokes the seat. The membership row is kept so the audit log stays whole.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaceId = await Resolve<ScimAuthenticator>().ResolveWorkspaceAsync(HttpContext, ct);
        if (workspaceId is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var member = await ScimMapper.FindAsync(db, workspaceId.Value, Route<Guid>("Id"), ct);
        if (member is null)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status404NotFound, ct);
            return;
        }

        await ScimPatchUserEndpoint.SetActiveAsync(db, workspaceId.Value, member, false, ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>Reading the two request shapes identity providers actually send.</summary>
internal static class ScimFilter
{
    /// <summary>Parses <c>userName eq "value"</c>. Anything else is ignored, deliberately.</summary>
    public static bool TryReadUserName(string? filter, out string userName)
    {
        userName = string.Empty;
        if (string.IsNullOrWhiteSpace(filter))
            return false;

        const string Prefix = "userName eq ";
        var trimmed = filter.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        userName = trimmed[Prefix.Length..].Trim().Trim('"').ToLowerInvariant();
        return userName.Length > 0;
    }

    /// <summary>
    /// Finds the `active` value in a PatchOp. Providers disagree on shape — Okta sends
    /// <c>{path: "active", value: false}</c>, Entra sends <c>{value: {"active": false}}</c> —
    /// so both are read rather than picking one and being wrong for half the market.
    /// </summary>
    public static bool? ReadActive(List<ScimPatchOperation>? operations)
    {
        if (operations is null)
            return null;

        foreach (var op in operations)
        {
            if (string.Equals(op.Path, "active", StringComparison.OrdinalIgnoreCase))
            {
                if (op.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return op.Value.GetBoolean();

                // Okta has also been seen sending the boolean as a string.
                if (op.Value.ValueKind == JsonValueKind.String && bool.TryParse(op.Value.GetString(), out var parsed))
                    return parsed;
            }

            if (op.Value.ValueKind == JsonValueKind.Object
                && op.Value.TryGetProperty("active", out var nested)
                && nested.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return nested.GetBoolean();
            }
        }

        return null;
    }
}

/// <summary>Shared mapping and lookups, so the five endpoints agree on one representation.</summary>
internal static class ScimMapper
{
    public static Task<WorkspaceMember?> FindAsync(AppDbContext db, Guid workspaceId, Guid id, CancellationToken ct)
        // Scoped by workspace, never by id alone: the token is the tenant boundary (#190).
        => db.WorkspaceMembers.FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == workspaceId, ct);

    public static async Task<string?> NameAsync(AppDbContext db, Guid? userId, CancellationToken ct)
        => userId is null
            ? null
            : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync(ct);

    public static ScimUser Map(WorkspaceMember member, string? name) => new(
        [ScimSchemas.User],
        member.Id.ToString(),
        member.InviteEmail,
        new ScimName(name),
        [new ScimEmail(member.InviteEmail, true)],
        member.RevokedAt is null,
        new ScimMeta(ScimSchemas.ResourceType, member.CreatedAt, member.UpdatedAt ?? member.CreatedAt));
}
