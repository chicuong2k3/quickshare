# Story 1.1: Storage Backend Foundation

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.1
- **Status:** ready-for-dev

---

## User Story

> As a developer,
> I want the storage provider abstraction and core entity model in place,
> So that all upload/download operations have a consistent backend.

---

## Requirements

### Business Value

This story establishes the architectural foundation for the entire ZapDrop storage backend. It replaces the current Telegram-backed storage (WTelegram.Client → Telegram channel messages) with an owned disk storage abstraction. Without it, no delivery pipeline can be built.

### From the PRD

The pivot from Telegram to owned storage is the core strategic change (PRD §0). FR-1 (split-and-store any-size file) and FR-3 (download reassembly) require `IStorageProvider` as the foundation. AD-1 (Storage Provider Abstraction) mandates that all storage operations go through this interface.

### From the Architecture Spine

Key architectural decisions binding this story:

| AD | Rule | Implementation Impact |
|---|---|---|
| AD-1 | All storage through `IStorageProvider` | Create interface + LocalDisk impl. No direct filesystem in handlers. |
| AD-6 | Entity model: Delivery → Manifest → Chunks | EF Core entities with FK cascade relationships |
| AD-7 | Encrypted Manifest as `bytea` in PostgreSQL | `Manifest` table with bytea column, co-located with Delivery |
| AD-8 | Clean-break migration from legacy entities | New entities via EF migration; legacy File/TempShare preserved |
| AD-12 | LightResults `Result<T>` error handling | Match existing patterns |
| AD-13 | Storage provider config-driven | `TeleCloudOptions.StorageProvider` determines provider |
| AD-14 | Background cleanup job | Infrastructure for HostedService (no scheduling logic yet) |

---

## Technical Requirements

### 1. IStorageProvider Interface

Create `src/TeleCloud.Server/Infrastructure/IStorageProvider.cs`:

```csharp
namespace TeleCloud.Server.Infrastructure;

public interface IStorageProvider
{
    /// <summary>Stores a chunk and returns a storage reference string.</summary>
    Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default);

    /// <summary>Retrieves chunk bytes by storage reference.</summary>
    Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default);

    /// <summary>Deletes chunks by their storage references.</summary>
    Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default);
}
```

### 2. LocalDiskStorageProvider

Create `src/TeleCloud.Server/Infrastructure/LocalDiskStorageProvider.cs`:

- Reads root path from `TeleCloudOptions.LocalDisk.Path` (configured in appsettings)
- Stores chunks as `{rootPath}/{chunkId}.chunk` — flat file per chunk
- Creates root directory on first use if it doesn't exist
- Implements all three `IStorageProvider` methods
- Uses `AsyncFile.WriteAllBytesAsync` / `ReadAllBytesAsync` / `File.Delete`
- Logs storage operations via `ILogger<LocalDiskStorageProvider>`

### 3. New Entities

Create in `src/TeleCloud.Server/Data/`:

#### Delivery (`Delivery.cs`)
```csharp
namespace TeleCloud.Server.Data;

public record Delivery
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public long TotalBytes { get; init; }  // Total ciphertext size
    public int ChunkCount { get; init; }
    public string ShareCode { get; init; } = string.Empty;  // URL-safe base64
    public int MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public long EgressBytes { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? PasswordHash { get; set; }  // BCrypt hash, null = no password
    public string? BrandLogo { get; set; }
    public string? BrandMessage { get; set; }
    public string? BrandAccent { get; set; }
    public decimal? Price { get; set; }  // null = free/non-paid
    public string? PaymentStatus { get; set; }  // null/unpaid/paid
    public DateTime CreatedAt { get; init; }
    public User? Sender { get; init; }
    public Manifest? Manifest { get; init; }
}
```

#### Manifest (`Manifest.cs`)
```csharp
namespace TeleCloud.Server.Data;

public record Manifest
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public byte[] EncryptedData { get; init; } = [];  // bytea, encrypted payload
    public DateTime CreatedAt { get; init; }
    public Delivery? Delivery { get; init; }
}
```

#### Chunk (`Chunk.cs`)
```csharp
namespace TeleCloud.Server.Data;

public record Chunk
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public int Index { get; init; }
    public long Size { get; init; }
    public string StorageRef { get; init; } = string.Empty;  // IStorageProvider reference
    public string Sha256 { get; init; } = string.Empty;  // hex-encoded SHA-256 of ciphertext
    public DateTime CreatedAt { get; init; }
    public Delivery? Delivery { get; init; }
}
```

#### UploadSession (`UploadSession.cs`)
```csharp
namespace TeleCloud.Server.Data;

public record UploadSession
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Status { get; set; } = "InProgress";  // InProgress / Committed / Expired
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }  // 24h TTL
}
```

#### DownloadTracking (`DownloadTracking.cs`)
```csharp
namespace TeleCloud.Server.Data;

public record DownloadTracking
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public DateTime DownloadedAt { get; init; }
}
```

### 4. Entity Relationship Rules

| Relationship | Type | Delete Behavior |
|---|---|---|
| Delivery → Manifest | 1:1 | Cascade |
| Delivery → Chunks | 1:N | Cascade |
| Delivery → User (Sender) | N:1 | Restrict |
| Delivery → DownloadTracking | 1:N | Cascade |
| UploadSession → User (Sender) | N:1 | Cascade |

### 5. DbContext Updates

Update `TeleCloudDbContext.cs` to add:

```csharp
public DbSet<Delivery> Deliveries => Set<Delivery>();
public DbSet<Manifest> Manifests => Set<Manifest>();
public DbSet<Chunk> Chunks => Set<Chunk>();
public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
public DbSet<DownloadTracking> DownloadTrackings => Set<DownloadTracking>();
```

Add `OnModelCreating` configuration for each new entity:
- `Delivery`: ToTable("deliveries"), indexes on SenderId, ShareCode, ExpiresAt
- `Manifest`: ToTable("manifests"), co-located with Delivery
- `Chunk`: ToTable("chunks"), index on (DeliveryId, Index) unique
- `UploadSession`: ToTable("upload_sessions"), index on ExpiresAt, index on SenderId
- `DownloadTracking`: ToTable("download_tracking"), index on DeliveryId, index on DownloadedAt

### 6. Configuration Updates

Update `TeleCloudOptions.cs` to add:

```csharp
public string StorageProvider { get; init; } = "LocalDisk";
public LocalDiskOptions LocalDisk { get; init; } = new();

public sealed class LocalDiskOptions
{
    public string RootPath { get; init; } = "./storage/chunks";
}
```

### 7. DI Registration

In `Program.cs`, register:

```csharp
// Storage provider (config-switched singleton)
var storageProvider = builder.Configuration["TeleCloud:StorageProvider"] switch
{
    "LocalDisk" or null => (IStorageProvider)new LocalDiskStorageProvider(
        Options.Create(builder.Configuration.GetSection("TeleCloud:LocalDisk").Get<LocalDiskOptions>() ?? new()),
        sp.GetRequiredService<ILogger<LocalDiskStorageProvider>>()),
    _ => throw new InvalidOperationException($"Unknown StorageProvider: {builder.Configuration["TeleCloud:StorageProvider"]}")
};
builder.Services.AddSingleton<IStorageProvider>(storageProvider);
```

Or cleaner using options pattern + factory registration.

### 8. EF Core Migration

Generate migration: `dotnet ef migrations add AddDeliveryEntities --context TeleCloudDbContext`

---

## Codebase Patterns to Follow

### Existing Patterns (Must Match)

**Record types:** All entities use C# `record` with `init` setters (see `FileMetadata.cs`, `TempShare.cs`, `User.cs`).

**Namespace:** `TeleCloud.Server.Data` for entities, `TeleCloud.Server.Infrastructure` for interfaces/providers.

**DbContext:** Single `TeleCloudDbContext` class with `OnModelCreating` for Fluent API config. Table names in snake_case via `ToTable()`.

**Result pattern:** `LightResults.Result<T>` for error handling (see existing handlers).

**DI registration:** In `Program.cs` with explicit singleton/scoped as appropriate.

### File Location Summary

| New File | Path |
|---|---|
| IStorageProvider | `src/TeleCloud.Server/Infrastructure/IStorageProvider.cs` |
| LocalDiskStorageProvider | `src/TeleCloud.Server/Infrastructure/LocalDiskStorageProvider.cs` |
| Delivery entity | `src/TeleCloud.Server/Data/Delivery.cs` |
| Manifest entity | `src/TeleCloud.Server/Data/Manifest.cs` |
| Chunk entity | `src/TeleCloud.Server/Data/Chunk.cs` |
| UploadSession entity | `src/TeleCloud.Server/Data/UploadSession.cs` |
| DownloadTracking entity | `src/TeleCloud.Server/Data/DownloadTracking.cs` |

| Modified File | Change |
|---|---|
| `TeleCloudDbContext.cs` | Add 5 new DbSets + entity configs |
| `TeleCloudOptions.cs` | Add StorageProvider + LocalDiskOptions |
| `Program.cs` | Register IStorageProvider singleton, add storage config binding |

---

## Acceptance Criteria

### AC-1: IStorageProvider resolves
**Given** the app is configured with `StorageProvider: "LocalDisk"`  
**When** DI resolves `IStorageProvider`  
**Then** `LocalDiskStorageProvider` singleton returned with configured path

### AC-2: Chunk storage round-trip
**Given** `LocalDiskStorageProvider` initialized with root path  
**When** `StoreChunkAsync` called with chunk data  
**Then** data written to `{rootPath}/{chunkId}.chunk`, returns storage reference string  
**When** `GetChunkAsync` called with that reference  
**Then** original byte data returned

### AC-3: Chunk deletion
**Given** stored chunks exist  
**When** `DeleteChunksAsync` called with their references  
**Then** all specified chunk files removed from disk; non-existent files silently skipped

### AC-4: EF Core migration applies
**Given** pending migration `AddDeliveryEntities`  
**When** `dotnet ef database update` runs  
**Then** tables created: `deliveries`, `manifests`, `chunks`, `upload_sessions`, `download_tracking` with correct columns and FK constraints

### AC-5: Entity FK cascade
**Given** a Delivery with associated Manifest, Chunks, and DownloadTracking records  
**When** Delivery row deleted  
**Then** Manifest, Chunks, and DownloadTracking cascade-deleted

### AC-6: Legacy tables preserved
**Given** existing `files`, `folders`, `temp_shares` tables  
**When** migration runs  
**Then** legacy tables are unchanged and still queryable

### AC-7: UploadSession 24h TTL
**Given** UploadSession created with `ExpiresAt = CreatedAt + 24h`  
**When** queried  
**Then** ExpiresAt is correctly set to 24 hours after CreatedAt

---

## Dev Notes

### Brownfield Considerations

- **Do NOT remove** existing `FileMetadata`, `TempShare`, `Folder` entities or their DbSets — they're still in use by legacy features
- **Do NOT modify** existing `TelegramService` or `WTelegram.Client` registration yet — those are replaced in later stories
- **Do NOT implement** encryption (Story 1.4), cleanup jobs, or upload flows — this is purely the storage foundation
- The migration strategy (AD-8) is clean-break: new entities alongside deprecated legacy ones

### Key Architecture Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Manifest storage | PostgreSQL bytea (AD-7) | ACID queries, co-located with Delivery, no cross-provider complexity |
| Chunk storage | Disk files via IStorageProvider | AD-1 abstraction, switchable to B2 later |
| Entity records | `record` with `init` | Matches existing codebase pattern |
| Provider config | `TeleCloudOptions.StorageProvider` | Config-driven switching, matches AD-13 |

### Entity Design Notes for Future Stories

- `Delivery.PasswordHash` will be used in Story 2.1 (password gate)
- `Delivery.BrandLogo/BrandMessage/BrandAccent` will be used in Story 2.2 (branded page)
- `Delivery.Price/PaymentStatus` will be used in Story 2.4 (paid delivery via Ko-fi)
- `DownloadTracking` will be used in Story 2.3 (download tracking)
- `UploadSession` will be used in Story 1.2 (chunked upload API)
- `Chunk.Sha256` will be used in Story 1.3 (download integrity) and Story 1.4 (E2E encryption AAD binding)

### Testing Guidance

- Unit test `LocalDiskStorageProvider` with a temp directory
- Verify FK cascade delete behavior via integration test with in-memory database
- Verify migration can apply and rollback cleanly
- Test that concurrent writes don't collide (different chunk IDs)

### Common Pitfalls to Avoid

- ❌ Don't make `IStorageProvider` async methods blocking — use `AsyncFile` APIs
- ❌ Don't forget `CancellationToken` propagation on all async methods
- ❌ Don't use `Directory.GetCurrentDirectory()` for root path — use configured value
- ❌ Don't create entities without `Guid.CreateVersion7()` for IDs
- ❌ Don't delete the old `Files/TempShares` DbSets — they're still referenced in existing handlers
