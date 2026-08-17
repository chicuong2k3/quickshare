# Sender Email Capture + Verify (issue #76) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture optional sender email on anonymous upload, store it on `Delivery`, email the share link with a single-use confirm link and an unsubscribe link, all rate-limited.

**Architecture:** Email is captured at `StartR2` (stored on `UploadSession.SenderEmail`), copied to `Delivery` at `CommitR2` (never from the commit request), then the link email is sent after the commit succeeds. Confirm/unsubscribe are DataProtection-signed tokens (pattern: existing `UnsubscribeTokens`) rendered as HTML pages (pattern: `UnsubscribeEndpoint`). Email capture is rate-limited in-memory per email + per IP; over the limit the email is skipped but the upload always succeeds.

**Tech Stack:** ASP.NET Core 10 (Blazor SSR), FastEndpoints, Mediator (martinothamar/Mediator), EF Core 10 + Npgsql, MailKit/SMTP (Brevo), DataProtection, xUnit + TestWebAppFactory.

## Global Constraints

- `string.Empty`, never `""` — except compile-time constant contexts.
- No magic strings: protocol ids, HTTP headers, route paths, statuses, config keys MUST be centralized in `static class` constants (e.g. `RoutePaths`, `ApiTags`).
- FastEndpoints auth: `AllowAnonymous()` only for public endpoints; authenticated endpoints declare nothing.
- User-facing strings MUST live in `SharedLoc.en.resx` / `SharedLoc.vi.resx` — never hardcoded in a page; JS gets strings via a JSON block (pattern: `upload-i18n`).
- Audit fields (`CreatedAt`/`UpdatedAt`) are set by `AuditableEntityInterceptor` — never set manually.
- One statement per line. Entity → DTO mappings via Mapperly unless a LINQ projection is required.
- Email content: Vietnamese (matches `DownloadNotificationJob`).
- Email failure must NEVER fail the upload — log and drop (pattern: `DownloadNotificationJob.NotifyAsync`).
- R2 commit metadata comes from the validated session, never the commit request.

---

### Task 1: Data model — `UploadSession.SenderEmail` + `Delivery` sender-email fields + migration

**Files:**
- Modify: `src/QuickShare/Data/Entities/UploadSession.cs`
- Modify: `src/QuickShare/Data/Entities/Delivery.cs`
- Create: `src/QuickShare/Data/Migrations/20260815XXXXXX_SenderEmailCapture.cs` (+ `.Designer.cs`, `AppDbContextModelSnapshot.cs` changes — generated)

**Interfaces:**
- Consumes: nothing (entity-only task).
- Produces: `UploadSession.SenderEmail` (string?, nullable), `Delivery.SenderEmail` (string?, nullable), `Delivery.SenderEmailConfirmed` (bool), `Delivery.SenderEmailConsent` (bool). Used by Tasks 2, 4, 5, 6.

- [ ] **Step 1: Add `SenderEmail` to `UploadSession`**

```csharp
namespace QuickShare.Data.Entities;

[DynamicSort]
public record UploadSession : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; init; }
    public string FileName { get; init; } = string.Empty;
    // ...rest unchanged
}
```

- [ ] **Step 2: Add sender-email fields to `Delivery`**

```csharp
[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; set; }
    public bool SenderEmailConfirmed { get; set; }
    public bool SenderEmailConsent { get; set; }
    // ...rest unchanged
}
```

(`set`, not `init`, because Tasks 5/6 mutate `SenderEmailConfirmed`/`SenderEmailConsent` after creation.)

- [ ] **Step 3: Generate the migration**

Run from repo root:

```powershell
rtk dotnet ef migrations add SenderEmailCapture --project src/QuickShare
```

Expected: new files under `src/QuickShare/Data/Migrations/` with `AddColumn("SenderEmail", "text", nullable: true)` on `deliveries` and `upload_sessions`, and `AddColumn<bool>("SenderEmailConfirmed", ...)` + `AddColumn<bool>("SenderEmailConsent", ...)` on `deliveries`.

- [ ] **Step 4: Build to verify**

```powershell
rtk dotnet build src/QuickShare/QuickShare.csproj --no-restore
```

Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare/Data/Entities/UploadSession.cs src/QuickShare/Data/Entities/Delivery.cs src/QuickShare/Data/Migrations/
git commit -m "feat(data): sender email fields on UploadSession and Delivery (issue #76)"
```

---

### Task 2: `SenderEmailTokens` — DataProtection tokens with expiry

**Files:**
- Create: `src/QuickShare/Infrastructure/Email/SenderEmailTokens.cs`
- Modify: `src/QuickShare/Program.cs` (register singleton)

**Interfaces:**
- Consumes: `Delivery.Id` (Guid) from Task 1.
- Produces: `SenderEmailTokens.CreateConfirmToken(Guid deliveryId, DateTimeOffset? expiresAt = null) : string`, `SenderEmailTokens.TryReadConfirmToken(string token, out Guid deliveryId, out DateTimeOffset expiresAt) : bool`, `CreateUnsubscribeToken(...)`, `TryReadUnsubscribeToken(...)`, `SenderEmailTokens.DefaultExpiry` (TimeSpan = 24h). Used by Tasks 4, 5, 6, 10.

- [ ] **Step 1: Write the failing unit test**

Create `tests/QuickShare.Server.Tests/Infrastructure/SenderEmailTokensTests.cs`:

```csharp
using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class SenderEmailTokensTests
{
    [Fact]
    public void ConfirmToken_RoundTrips_DeliveryId()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var token = tokens.CreateConfirmToken(id);
        var ok = tokens.TryReadConfirmToken(token, out var read, out _);

        Assert.True(ok);
        Assert.Equal(id, read);
    }

    [Fact]
    public void ConfirmToken_Expired_IsRejected()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var token = tokens.CreateConfirmToken(id, DateTimeOffset.UtcNow.AddHours(-1));
        var ok = tokens.TryReadConfirmToken(token, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ConfirmToken_Invalid_IsRejected()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);

        Assert.False(tokens.TryReadConfirmToken("garbage", out _, out _));
    }

    [Fact]
    public void ConfirmAndUnsubscribeTokens_DoNotInteroperate()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var confirm = tokens.CreateConfirmToken(id);
        var unsubscribe = tokens.CreateUnsubscribeToken(id);

        Assert.False(tokens.TryReadUnsubscribeToken(confirm, out _, out _));
        Assert.False(tokens.TryReadConfirmToken(unsubscribe, out _, out _));
    }
}

/// <summary>Minimal DataProtection provider so tokens work without the app host.</summary>
internal sealed class TestDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose) =>
        new EphemeralDataProtector(purpose);
}

internal sealed class EphemeralDataProtector(string purpose) : IDataProtector
{
    private readonly byte[] _key =
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(purpose));

    public IDataProtector CreateProtector(string subPurpose) =>
        new EphemeralDataProtector(purpose + "/" + subPurpose);

    public byte[] Protect(byte[] plaintext)
    {
        var output = new byte[plaintext.Length + 16];
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        Buffer.BlockCopy(aes.IV, 0, output, 0, 16);
        using var enc = aes.CreateEncryptor();
        var ct = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
        Buffer.BlockCopy(ct, 0, output, 16, ct.Length);
        return output;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        var iv = new byte[16];
        Buffer.BlockCopy(protectedData, 0, iv, 0, 16);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(protectedData, 16, protectedData.Length - 16);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailTokensTests"
```

Expected: FAIL — `SenderEmailTokens` type not found.

- [ ] **Step 3: Implement `SenderEmailTokens`**

Create `src/QuickShare/Infrastructure/Email/SenderEmailTokens.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// Signed, URL-safe tokens for anonymous-sender email actions (confirm / unsubscribe).
/// Reuses the app's DataProtection keyring — no extra secret to configure or rotate.
/// Distinct purposes keep confirm and unsubscribe tokens from interoperating.
/// </summary>
public sealed class SenderEmailTokens(IDataProtectionProvider provider)
{
    private const string ConfirmPurpose = "quickshare.sender-email.confirm";
    private const string UnsubscribePurpose = "quickshare.sender-email.unsubscribe";

    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly IDataProtector _confirm = provider.CreateProtector(ConfirmPurpose);
    private readonly IDataProtector _unsubscribe = provider.CreateProtector(UnsubscribePurpose);

    public string CreateConfirmToken(Guid deliveryId, DateTimeOffset? expiresAt = null)
        => Create(_confirm, deliveryId, expiresAt);

    public bool TryReadConfirmToken(string token, out Guid deliveryId, out DateTimeOffset expiresAt)
        => TryRead(_confirm, token, out deliveryId, out expiresAt);

    public string CreateUnsubscribeToken(Guid deliveryId, DateTimeOffset? expiresAt = null)
        => Create(_unsubscribe, deliveryId, expiresAt);

    public bool TryReadUnsubscribeToken(string token, out Guid deliveryId, out DateTimeOffset expiresAt)
        => TryRead(_unsubscribe, token, out deliveryId, out expiresAt);

    private static string Create(IDataProtector protector, Guid deliveryId, DateTimeOffset? expiresAt)
    {
        var expiry = (expiresAt ?? DateTimeOffset.UtcNow.Add(DefaultExpiry)).ToUnixTimeSeconds();
        return protector.Protect($"{deliveryId:N}|{expiry}");
    }

    private static bool TryRead(IDataProtector protector, string token, out Guid deliveryId, out DateTimeOffset expiresAt)
    {
        deliveryId = Guid.Empty;
        expiresAt = default;

        try
        {
            var parts = protector.Unprotect(token).Split('|');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out deliveryId))
                return false;

            if (!long.TryParse(parts[1], out var unix))
                return false;

            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            return expiresAt > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/QuickShare/Program.cs`, next to the existing `UnsubscribeTokens` registration:

```csharp
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.SenderEmailTokens>();
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailTokensTests"
```

Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/QuickShare/Infrastructure/Email/SenderEmailTokens.cs src/QuickShare/Program.cs tests/QuickShare.Server.Tests/Infrastructure/SenderEmailTokensTests.cs
git commit -m "feat(email): DataProtection tokens for sender-email confirm and unsubscribe"
```

---

### Task 3: StartR2 — capture optional sender email

**Files:**
- Modify: `src/QuickShare/Features/Uploads/StartR2/StartR2Request.cs` (StartR2Request lives in `StartR2Endpoint.cs`)
- Modify: `src/QuickShare/Features/Uploads/StartR2/StartR2Endpoint.cs`
- Modify: `src/QuickShare/Features/Uploads/StartR2/StartR2Handler.cs`
- Modify: `src/QuickShare/Features/Uploads/StartR2/StartR2Validator.cs`

**Interfaces:**
- Consumes: `UploadSession.SenderEmail` (Task 1).
- Produces: `StartR2Request.SenderEmail` (string?), `StartR2Command.SenderEmail` (string?), `UploadSession.SenderEmail` persisted. Used by Task 4.

- [ ] **Step 1: Write the failing test**

In `tests/QuickShare.Server.Tests/E2E/UploadDownloadFlowTests.cs`, add:

```csharp
[Fact]
public async Task StartUpload_WithSenderEmail_StoresItOnSession()
{
    var client = CreateNoRedirectClient();
    var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
    {
        FileName = TestFileName,
        FileSize = TestFileSize,
        ContentType = TestContentType,
        PartNumbers = [1],
        SenderEmail = "sender@example.com",
    });
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
    var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
    Assert.NotNull(start);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var session = await db.UploadSessions.AsNoTracking().FirstAsync(s => s.Id == start.DeliveryId);
    Assert.Equal("sender@example.com", session.SenderEmail);
}

[Fact]
public async Task StartUpload_InvalidSenderEmail_Returns400()
{
    var client = CreateNoRedirectClient();
    var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
    {
        FileName = TestFileName,
        FileSize = TestFileSize,
        ContentType = TestContentType,
        PartNumbers = [1],
        SenderEmail = "not-an-email",
    });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~UploadDownloadFlowTests"
```

Expected: the two new tests FAIL (`SenderEmail` not found on `StartR2Request` / no validation).

- [ ] **Step 3: Add `SenderEmail` to request, command, endpoint, validator, handler**

`StartR2Endpoint.cs` — add to `StartR2Request`:

```csharp
public class StartR2Request
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public IReadOnlyList<int> PartNumbers { get; set; } = [];
    public string? SenderEmail { get; set; }
}
```

Same file, `HandleAsync`:

```csharp
var cmd = new StartR2Command(req.FileName, req.FileSize, req.ContentType, req.PartNumbers, req.SenderEmail);
```

`StartR2Handler.cs`:

```csharp
public record StartR2Command(string FileName, long FileSize, string ContentType, IReadOnlyList<int> PartNumbers, string? SenderEmail);
```

In `Handle`, on the session:

```csharp
var session = new UploadSession
{
    Id = deliveryId,
    SenderId = user?.Id,
    SenderEmail = string.IsNullOrWhiteSpace(cmd.SenderEmail) ? null : cmd.SenderEmail.Trim(),
    FileName = cmd.FileName,
    // ...rest unchanged
};
```

`StartR2Validator.cs` — add:

```csharp
RuleFor(x => x.SenderEmail)
    .EmailAddress()
    .WithMessage("A valid email address is required when provided.")
    .MaximumLength(320)
    .WithMessage("Email address must be 320 characters or fewer.");
```

(FluentValidation's `EmailAddress()` treats null/empty as valid, so the optional rule only fires when a value is present.)

- [ ] **Step 4: Run tests to verify they pass**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~UploadDownloadFlowTests"
```

Expected: PASS — both new tests plus all pre-existing ones.

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare/Features/Uploads/StartR2/ tests/QuickShare.Server.Tests/E2E/UploadDownloadFlowTests.cs
git commit -m "feat(upload): capture optional sender email at start-r2 (issue #76)"
```

---

### Task 4: CommitR2 — copy email to Delivery, consent flag, rate-limited link email

**Files:**
- Modify: `src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs`
- Create: `src/QuickShare/Infrastructure/Email/SenderEmailRateLimiter.cs`
- Modify: `src/QuickShare/Program.cs` (register limiter)

**Interfaces:**
- Consumes: `Delivery.SenderEmail` / `SenderEmailConsent` (Task 1), `SenderEmailTokens` (Task 2), `StartR2Command.SenderEmail` → `UploadSession.SenderEmail` (Task 3).
- Produces: email send with share link + confirm + unsubscribe URLs. Used by Task 10 (tests).

- [ ] **Step 0: Add RoutePaths constants (prerequisite for Task 4's email body)**

`src/QuickShare/Infrastructure/Constants/RoutePaths.cs` — add:

```csharp
public const string ConfirmSenderEmail = "/confirm-sender-email";
public const string UnsubscribeSender = "/unsubscribe-sender";
```

(The endpoint implementations land in Tasks 5 and 6; the constants must exist first so `CommitR2Handler` compiles.)

- [ ] **Step 1: Write the failing test**

In `tests/QuickShare.Server.Tests/E2E/UploadDownloadFlowTests.cs`, add:

```csharp
[Fact]
public async Task Commit_WithSenderEmail_CopiesEmailAndSetsConsent()
{
    var client = CreateNoRedirectClient();
    var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
    {
        FileName = TestFileName,
        FileSize = TestFileSize,
        ContentType = TestContentType,
        PartNumbers = [1],
        SenderEmail = "sender@example.com",
    });
    var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
    Assert.NotNull(start);

    var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
    {
        DeliveryId = start.DeliveryId,
        UploadId = start.UploadId,
        ObjectKey = start.ObjectKey,
        FileName = TestFileName,
        ContentType = TestContentType,
        FileSize = TestFileSize,
        Parts = [new R2PartEtag(1, "etag-1")],
    });
    Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == start.DeliveryId);
    Assert.Equal("sender@example.com", delivery.SenderEmail);
    Assert.True(delivery.SenderEmailConsent);
    Assert.False(delivery.SenderEmailConfirmed);
}

[Fact]
public async Task Commit_WithoutSenderEmail_LeavesEmailNull()
{
    var client = CreateNoRedirectClient();
    var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
    {
        FileName = TestFileName,
        FileSize = TestFileSize,
        ContentType = TestContentType,
        PartNumbers = [1],
    });
    var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
    Assert.NotNull(start);

    var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
    {
        DeliveryId = start.DeliveryId,
        UploadId = start.UploadId,
        ObjectKey = start.ObjectKey,
        FileName = TestFileName,
        ContentType = TestContentType,
        FileSize = TestFileSize,
        Parts = [new R2PartEtag(1, "etag-1")],
    });
    Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == start.DeliveryId);
    Assert.Null(delivery.SenderEmail);
    Assert.False(delivery.SenderEmailConsent);
}
```

Also create `tests/QuickShare.Server.Tests/Infrastructure/SenderEmailRateLimiterTests.cs`:

```csharp
using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class SenderEmailRateLimiterTests
{
    [Fact]
    public void SameEmail_SixthAttemptInWindow_Denied()
    {
        var limiter = new SenderEmailRateLimiter();
        const string email = "a@example.com";
        const string ip = "10.0.0.1";

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAllow(email, ip));

        Assert.False(limiter.TryAllow(email, ip));
    }

    [Fact]
    public void SameIp_TwentyFirstAttemptInWindow_Denied()
    {
        var limiter = new SenderEmailRateLimiter();
        const string ip = "10.0.0.2";

        for (var i = 0; i < 20; i++)
            Assert.True(limiter.TryAllow($"u{i}@example.com", ip));

        Assert.False(limiter.TryAllow("u20@example.com", ip));
    }

    [Fact]
    public void DifferentEmail_SameIp_Allowed()
    {
        var limiter = new SenderEmailRateLimiter();

        Assert.True(limiter.TryAllow("a@example.com", "10.0.0.3"));
        Assert.True(limiter.TryAllow("b@example.com", "10.0.0.3"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~UploadDownloadFlowTests|FullyQualifiedName~SenderEmailRateLimiterTests"
```

Expected: the commit-copy tests FAIL (fields missing on `Delivery`), the limiter tests FAIL (type not found).

- [ ] **Step 3: Implement `SenderEmailRateLimiter`**

Create `src/QuickShare/Infrastructure/Email/SenderEmailRateLimiter.cs`:

```csharp
using System.Collections.Concurrent;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// In-memory rate limiter for anonymous sender-email capture (issue #76).
/// One window per email (5 deliveries/hour) and one per IP (20 captures/hour).
/// Over the limit the link email is skipped — the upload itself always succeeds.
/// </summary>
public sealed class SenderEmailRateLimiter
{
    private const int MaxPerEmail = 5;
    private const int MaxPerIp = 20;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, RateWindow> _windows = new();

    public bool TryAllow(string email, string ip)
    {
        var now = DateTime.UtcNow;
        return Allow($"email:{email}", MaxPerEmail, now) && Allow($"ip:{ip}", MaxPerIp, now);
    }

    private bool Allow(string key, int limit, DateTime now)
    {
        var window = _windows.GetOrAdd(key, _ => new RateWindow(now));

        lock (window)
        {
            if (now - window.Start > Window)
            {
                window.Start = now;
                window.Count = 0;
            }

            window.Count++;
            return window.Count <= limit;
        }
    }

    private sealed class RateWindow
    {
        public DateTime Start;
        public int Count;

        public RateWindow(DateTime start)
        {
            Start = start;
            Count = 0;
        }
    }
}
```

- [ ] **Step 4: Register limiter in DI**

In `src/QuickShare/Program.cs`, next to the other Email singletons:

```csharp
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.SenderEmailRateLimiter>();
```

- [ ] **Step 5: Wire CommitR2Handler**

`src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs` — add usings:

```csharp
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Email;
```

Change the constructor and add fields:

```csharp
public sealed class CommitR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    EmailSender emailSender,
    SenderEmailTokens senderEmailTokens,
    SenderEmailRateLimiter senderEmailRateLimiter,
    IOptions<EmailOptions> emailOptions,
    ILogger<CommitR2Handler> logger)
{
    // Client-side AES-GCM adds a 12-byte nonce + 16-byte tag on top of the declared plain size.
    private const long EncryptionOverheadSlack = 1024;
    private const string LinkEmailSubject = "Link tải file của bạn trên QuickShare";

    private readonly EmailOptions _emailOptions = emailOptions.Value;
```

In the `Delivery` initializer, after `SenderId = user?.Id,`:

```csharp
SenderId = user?.Id,
SenderEmail = session.SenderEmail,
SenderEmailConsent = !string.IsNullOrWhiteSpace(session.SenderEmail),
```

After `await db.SaveChangesAsync(ct);` and before building `baseUrl`, insert the email send:

```csharp
if (!string.IsNullOrWhiteSpace(delivery.SenderEmail) && emailSender.IsConfigured)
{
    await SendLinkEmailAsync(delivery, shareCode, ct);
}
```

Add the private method at the bottom of the class:

```csharp
/// <summary>
/// Emails the anonymous sender their share link, with a single-use confirm link and an
/// unsubscribe link. Best-effort: a send failure or rate-limit hit must not fail the upload.
/// </summary>
private async Task SendLinkEmailAsync(Delivery delivery, string shareCode, CancellationToken ct)
{
    var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!senderEmailRateLimiter.TryAllow(delivery.SenderEmail!, ip))
    {
        logger.LogWarning("Sender email rate limit hit for {Email}; skipping link email", delivery.SenderEmail);
        return;
    }

    var baseUrl = _emailOptions.BaseUrl.TrimEnd('/');
    var shareUrl = $"{baseUrl}/d/{shareCode}";
    var confirmUrl = $"{baseUrl}{RoutePaths.ConfirmSenderEmail}/{senderEmailTokens.CreateConfirmToken(delivery.Id)}";
    var unsubscribeUrl = $"{baseUrl}{RoutePaths.UnsubscribeSender}/{senderEmailTokens.CreateUnsubscribeToken(delivery.Id)}";

    var body = $"""
        <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
          <p>Chào bạn,</p>
          <p>File bạn gửi qua QuickShare đã sẵn sàng. Link chia sẻ:</p>
          <p><a href="{shareUrl}" style="color:#2563eb">{shareUrl}</a></p>
          <p>Xác nhận email của bạn để nhận thông báo khi file được tải: <a href="{confirmUrl}">Xác nhận email</a>.</p>
          <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
          <p style="font-size:13px;color:#6b7280">
            Bạn nhận email này vì đã chọn nhận link và cập nhật khi gửi file.
            Không muốn nhận nữa? <a href="{unsubscribeUrl}">Huỷ đăng ký</a>.
          </p>
        </div>
        """;

    try
    {
        await emailSender.SendAsync(delivery.SenderEmail!, "QuickShare", LinkEmailSubject, body, ct);
        logger.LogInformation("Sent sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
    }
    catch (Exception ex)
    {
        // ponytail: log and drop — a failed sender email isn't worth a retry queue.
        logger.LogWarning(ex, "Failed to send sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
    }
}
```

Note: `RoutePaths.ConfirmSenderEmail` and `RoutePaths.UnsubscribeSender` are added in Task 4 Step 0 (above), so this task compiles standalone. The endpoint implementations land in Tasks 5 and 6.

- [ ] **Step 6: Run tests to verify they pass**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~UploadDownloadFlowTests|FullyQualifiedName~SenderEmailRateLimiterTests"
```

Expected: PASS. (TestWebAppFactory does not configure SMTP, so `emailSender.IsConfigured` is false and no email is sent — the copy/consent assertions still validate the persistence path.)

- [ ] **Step 7: Commit**

```bash
git add src/QuickShare/Features/Uploads/CommitR2/CommitR2Handler.cs src/QuickShare/Infrastructure/Email/SenderEmailRateLimiter.cs src/QuickShare/Program.cs tests/QuickShare.Server.Tests/
git commit -m "feat(upload): copy sender email to delivery and send rate-limited link email (issue #76)"
```

---

### Task 5: Confirm-sender-email endpoint

**Files:**
- Create: `src/QuickShare/Features/Notifications/SenderConfirm/SenderConfirmEmailEndpoint.cs`

**Interfaces:**
- Consumes: `SenderEmailTokens.TryReadConfirmToken` (Task 2), `Delivery.SenderEmailConfirmed` (Task 1), `RoutePaths.ConfirmSenderEmail` (Task 4 Step 0).
- Produces: endpoint at `GET /confirm-sender-email/{token}` (used by Task 10).

- [ ] **Step 1: Write the failing test**

Create `tests/QuickShare.Server.Tests/E2E/SenderEmailConfirmTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Email;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class SenderEmailConfirmTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public SenderEmailConfirmTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_ValidToken_MarksEmailConfirmed()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.True(reloaded.SenderEmailConfirmed);
    }

    [Fact]
    public async Task Confirm_ExpiredToken_Returns400()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_Returns400()
    {
        var delivery = await SeedDeliveryAsync(confirmed: true);
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_GarbageToken_Returns400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/confirm-sender-email/not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Delivery> SeedDeliveryAsync(bool confirmed = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = $"seed-{Guid.NewGuid():N}"[..12],
            MaxDownloads = 100,
            StoragePath = "stub/seed",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            SenderEmail = "sender@example.com",
            SenderEmailConsent = true,
            SenderEmailConfirmed = confirmed,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailConfirmTests"
```

Expected: FAIL — 404 (route not found).

- [ ] **Step 3: Implement the endpoint**

Create `src/QuickShare/Features/Notifications/SenderConfirm/SenderConfirmEmailEndpoint.cs`:

```csharp
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Notifications.SenderConfirm;

/// <summary>
/// Marks an anonymous sender's email as confirmed. Linked from the sender link email
/// ("Xác nhận email"). Effectively single-use: an already-confirmed delivery rejects the token.
/// </summary>
// ponytail: no handler/validator slice — a single tracked flag flip doesn't need a message bus hop.
public sealed class SenderConfirmEmailEndpoint : EndpointWithoutRequest
{
    private const string TokenRouteKey = "token";
    private const string DonePage = "<p>Email của bạn đã được xác nhận. Bạn sẽ nhận thông báo khi file được tải.</p>";
    private const string InvalidPage = "<p>Link xác nhận không hợp lệ hoặc đã hết hạn.</p>";

    public override void Configure()
    {
        Get($"{RoutePaths.ConfirmSenderEmail}/{{{TokenRouteKey}}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Confirm anonymous sender email";
            s.Responses[200] = "Email confirmed.";
            s.Responses[400] = "Invalid, expired, or already-confirmed token.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Route<string>(TokenRouteKey)!;

        if (!Resolve<SenderEmailTokens>().TryReadConfirmToken(token, out var deliveryId, out _))
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.SenderEmail != null, ct);
        if (delivery is null || delivery.SenderEmailConfirmed)
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        delivery.SenderEmailConfirmed = true;
        await db.SaveChangesAsync(ct);

        await Send.StringAsync(DonePage, 200, MediaTypes.Html, ct);
    }
}
```

(`RoutePaths`, `ApiTags`, `MediaTypes` are global usings — mirror `UnsubscribeEndpoint`, which imports only `QuickShare.Infrastructure.Email`.)

- [ ] **Step 4: Run tests to verify they pass**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailConfirmTests"
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare/Features/Notifications/SenderConfirm/SenderConfirmEmailEndpoint.cs tests/QuickShare.Server.Tests/E2E/SenderEmailConfirmTests.cs
git commit -m "feat(notify): confirm-sender-email endpoint with single-use token (issue #76)"
```

---

### Task 6: Unsubscribe-sender endpoint

**Files:**
- Create: `src/QuickShare/Features/Notifications/SenderUnsubscribe/SenderUnsubscribeEndpoint.cs`

**Interfaces:**
- Consumes: `SenderEmailTokens.TryReadUnsubscribeToken` (Task 2), `Delivery.SenderEmailConsent` (Task 1), `RoutePaths.UnsubscribeSender` (Task 5).
- Produces: endpoint at `GET /unsubscribe-sender/{token}` (used by Task 10).

- [ ] **Step 1: Write the failing test**

Create `tests/QuickShare.Server.Tests/E2E/SenderEmailUnsubscribeTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.E2E;

public sealed class SenderEmailUnsubscribeTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public SenderEmailUnsubscribeTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Unsubscribe_ValidToken_SetsConsentFalse()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateUnsubscribeToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/unsubscribe-sender/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.False(reloaded.SenderEmailConsent);
    }

    [Fact]
    public async Task Unsubscribe_ExpiredToken_Returns400()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateUnsubscribeToken(delivery.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/unsubscribe-sender/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_GarbageToken_Returns400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/unsubscribe-sender/not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Delivery> SeedDeliveryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = $"seed-{Guid.NewGuid():N}"[..12],
            MaxDownloads = 100,
            StoragePath = "stub/seed",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            SenderEmail = "sender@example.com",
            SenderEmailConsent = true,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailUnsubscribeTests"
```

Expected: FAIL — 404 (route not found).

- [ ] **Step 3: Implement the endpoint**

Create `src/QuickShare/Features/Notifications/SenderUnsubscribe/SenderUnsubscribeEndpoint.cs`:

```csharp
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Notifications.SenderUnsubscribe;

/// <summary>
/// One-click opt-out from sender link emails. Linked at the bottom of the sender link email.
/// Distinct from /unsubscribe, which targets User.EmailOnDownload for digest emails.
/// </summary>
// ponytail: no handler/validator slice — a single tracked flag flip doesn't need a message bus hop.
public sealed class SenderUnsubscribeEndpoint : EndpointWithoutRequest
{
    private const string TokenRouteKey = "token";
    private const string DonePage = "<p>Đã huỷ nhận email cho file này. Bạn sẽ không nhận thêm thông báo nữa.</p>";
    private const string InvalidPage = "<p>Link huỷ đăng ký không hợp lệ hoặc đã hết hạn.</p>";

    public override void Configure()
    {
        Get($"{RoutePaths.UnsubscribeSender}/{{{TokenRouteKey}}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Unsubscribe from sender link emails for one delivery";
            s.Responses[200] = "Unsubscribed.";
            s.Responses[400] = "Invalid or tampered token.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Route<string>(TokenRouteKey)!;

        if (!Resolve<SenderEmailTokens>().TryReadUnsubscribeToken(token, out var deliveryId, out _))
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null)
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        delivery.SenderEmailConsent = false;
        await db.SaveChangesAsync(ct);

        await Send.StringAsync(DonePage, 200, MediaTypes.Html, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~SenderEmailUnsubscribeTests"
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare/Features/Notifications/SenderUnsubscribe/SenderUnsubscribeEndpoint.cs tests/QuickShare.Server.Tests/E2E/SenderEmailUnsubscribeTests.cs
git commit -m "feat(notify): unsubscribe-sender endpoint for sender link emails (issue #76)"
```

---

### Task 7: UI — email input on /send (anonymous only) + upload.js wiring

**Files:**
- Modify: `src/QuickShare/Components/Pages/Upload.razor`
- Modify: `src/QuickShare/Components/Pages/Upload.razor.css`
- Modify: `src/QuickShare/wwwroot/js/upload.js`
- Modify: `src/QuickShare/SharedLoc.en.resx`
- Modify: `src/QuickShare/SharedLoc.vi.resx`

**Interfaces:**
- Consumes: `StartR2Request.SenderEmail` (Task 3).
- Produces: `#upload-sender-email` input (anonymous only), `senderEmail` in StartR2 POST body.

- [ ] **Step 1: Add the email input to the idle section**

In `src/QuickShare/Components/Pages/Upload.razor`, after the OTP block (`</div>` closing `.encrypt-toggle`, line ~109) and before `<div id="upload-paste-tip">`, add:

```razor
@* Optional sender email — anonymous only: signed-in users already have email on their account. *@
@if (!isAuthenticated)
{
    <div class="sender-email-wrap">
        <label for="upload-sender-email" class="otp-label">@Loc["upload_sender_email_label"]</label>
        <input type="email" id="upload-sender-email" class="sender-email-input" inputmode="email" autocomplete="email" placeholder="@Loc["upload_sender_email_placeholder"]" />
        <span class="otp-hint">@Loc["upload_sender_email_hint"]</span>
    </div>
}
```

- [ ] **Step 2: Style the input**

In `src/QuickShare/Components/Pages/Upload.razor.css`, append:

```css
.sender-email-wrap {
    margin-top: 1.25rem;
    max-width: 24rem;
    margin-inline: auto;
    text-align: left;
}

.sender-email-input {
    width: 100%;
    margin-top: 0.375rem;
    padding: 0.625rem 0.875rem;
    border-radius: 0.5rem;
    border: 1px solid var(--db-color-border, #d1d5db);
    background: var(--db-color-surface, #ffffff);
    color: var(--db-color-text, #111827);
    font-size: 0.875rem;
}
```

- [ ] **Step 3: Wire upload.js**

In `src/QuickShare/wwwroot/js/upload.js`:

Add to the `state` object (after `source: SOURCE_WEB,`):

```js
senderEmail: '',
```

In `reset()` (add `state.senderEmail = '';` and clear the input):

```js
state.senderEmail = '';
var senderEmailEl = document.getElementById('upload-sender-email');
if (senderEmailEl)
    senderEmailEl.value = '';
```

In `startUpload(file, source)` after `state.source = source || SOURCE_WEB;`:

```js
state.senderEmail = (document.getElementById('upload-sender-email')?.value || '').trim();
```

In `doUpload`, the StartR2 body — add `senderEmail`:

```js
body: JSON.stringify({ fileName: file.name, fileSize: originalSize, contentType: file.type || 'application/octet-stream', partNumbers: partNumbers, senderEmail: state.senderEmail || null })
```

- [ ] **Step 4: Add i18n keys**

`src/QuickShare/SharedLoc.en.resx` — add:

```xml
<data name="upload_sender_email_label" xml:space="preserve"><value>Email me the link + updates</value></data>
<data name="upload_sender_email_placeholder" xml:space="preserve"><value>you@example.com</value></data>
<data name="upload_sender_email_hint" xml:space="preserve"><value>Optional — get your share link and download updates by email. Unsubscribe anytime.</value></data>
```

`src/QuickShare/SharedLoc.vi.resx` — add (UTF-8 Vietnamese):

```xml
<data name="upload_sender_email_label" xml:space="preserve"><value>Gửi link và cập nhật qua email cho tôi</value></data>
<data name="upload_sender_email_placeholder" xml:space="preserve"><value>ban@example.com</value></data>
<data name="upload_sender_email_hint" xml:space="preserve"><value>Tuỳ chọn — nhận link chia sẻ và thông báo khi file được tải. Huỷ bất cứ lúc nào.</value></data>
```

- [ ] **Step 5: Build + run browser test suite**

```powershell
rtk dotnet build src/QuickShare/QuickShare.csproj --no-restore
```

Expected: build succeeds (TS compile of upload.js runs via the project's build).

```powershell
rtk dotnet test tests/QuickShare.Browser.Tests/QuickShare.Browser.Tests.csproj --no-restore --filter "FullyQualifiedName~Upload"
```

Expected: PASS — anonymous upload flow still works with the optional email omitted.

- [ ] **Step 6: Commit**

```bash
git add src/QuickShare/Components/Pages/Upload.razor src/QuickShare/Components/Pages/Upload.razor.css src/QuickShare/wwwroot/js/upload.js src/QuickShare/SharedLoc.en.resx src/QuickShare/SharedLoc.vi.resx
git commit -m "feat(ui): optional sender email field on /send for anonymous uploads (issue #76)"
```

---

### Task 8: Full-suite verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full server test suite**

```powershell
rtk dotnet test tests/QuickShare.Server.Tests/QuickShare.Server.Tests.csproj --no-restore
```

Expected: all tests PASS (including pre-existing `UploadDownloadFlowTests`, `DeliveryExtrasApiTests`, `OrphanSweepJobTests`, `MonthlyQuotaTests`).

- [ ] **Step 2: Run the browser test suite**

```powershell
rtk dotnet test tests/QuickShare.Browser.Tests/QuickShare.Browser.Tests.csproj --no-restore
```

Expected: all tests PASS.

- [ ] **Step 3: Run the CI build command**

```powershell
rtk dotnet build --no-restore -warnaserror:CS8300
```

Expected: build succeeds, 0 errors, 0 warnings.

- [ ] **Step 4: Commit any leftover fixes**

```bash
git add -A
git commit -m "chore: fix CI findings from sender email capture"
```

(Only if Step 1–3 surfaced anything; otherwise skip this commit.)

---

### Task 9: Update issue #76 on GitHub

**Files:** none (GitHub only).

- [ ] **Step 1: Mark the issue as completed**

```powershell
gh issue close 76 --comment "Implemented: sender email capture on anonymous upload. SenderEmail stored on Delivery (nullable, consent opt-in at /send for anonymous users), link email sent after commit with single-use confirm token (SenderEmailConfirmed) and unsubscribe link (SenderEmailConsent=false), rate-limited per email (5/h) and per IP (20/h). Spec: docs/superpowers/specs/2026-08-15-sender-email-capture-design.md. Unblocks #29, #33, #34, #62."
```

Expected: issue 76 closed with the comment.

- [ ] **Step 2: Verify the issue state**

```powershell
gh issue view 76
```

Expected: status CLOSED with the comment visible.

---

## Self-Review Notes (run after writing, before execution)

1. **Spec coverage:** SenderEmail nullable on Delivery ✓ (T1, T4); optional capture at upload for anonymous ✓ (T3, T7); send link email reusing EmailSender ✓ (T4); consent + unsubscribe footer ✓ (T4, T6); rate-limit capture ✓ (T4); verify via single-use token ✓ (T2, T5); acceptance "sender gets link by email, future #29/#33/#34/#62 work" ✓ (email stored + confirmed flag ready).
2. **Type consistency:** `SenderEmailTokens.CreateConfirmToken(Guid, DateTimeOffset?)` and `TryReadConfirmToken(string, out Guid, out DateTimeOffset)` used identically in T2 (impl), T4 (call), T5 (call), T10-style tests (T5/T6 tests). `UploadSession.SenderEmail` init property set in T3 handler. `Delivery.SenderEmail`/`Confirmed`/`Consent` set in T1/T4/T5/T6 consistently.
3. **Task ordering dependency:** Task 4 references `RoutePaths.ConfirmSenderEmail`/`UnsubscribeSender`, defined in Task 4 Step 0. Tasks 5 and 6 add only the endpoint implementations. No cross-task compile gaps.
4. **Placeholder scan:** No TBD/TODO; all code complete.
